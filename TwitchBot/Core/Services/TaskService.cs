using Microsoft.Extensions.Logging;
using TwitchViewerBot.Core.Models;
using TwitchViewerBot.Core.Services;
using TwitchViewerBot.Core;
using System.Threading.Tasks;
using TwitchViewerBot.Data.Repositories;
using Microsoft.EntityFrameworkCore;

public class TaskService : ITaskService
{
    private readonly ILogger<TaskService> _logger;
    private readonly ITwitchService _twitchService;
    private readonly IProxyService _proxyService;
    private readonly IAccountService _accountService;
    private readonly ITaskRepository _taskRepository;

    private readonly List<BotTask> _tasks = new();
    private readonly Dictionary<int, CancellationTokenSource> _taskTokens = new();
    private readonly SemaphoreSlim _semaphore = new(20); // можно параметризовать
    public TaskService(

        ILogger<TaskService> logger,
        ITwitchService twitchService,
        IProxyService proxyService, IAccountService accountService, ITaskRepository taskRepository)
    {
        _logger = logger;
        _twitchService = twitchService;
        _proxyService = proxyService;
        _accountService = accountService;
        _taskRepository = taskRepository;
    }

    public async Task InitializeAsync()
    {
        var activeTasks = await _taskRepository.GetAll();
        activeTasks = [.. activeTasks.Where(t => t.Status == TwitchViewerBot.Core.Enums.TaskStatus.Pending || t.Status == TwitchViewerBot.Core.Enums.TaskStatus.Running || t.Status == TwitchViewerBot.Core.Enums.TaskStatus.Paused)];

        _tasks.Clear();
        _tasks.AddRange(activeTasks);

        _logger.LogInformation("Инициализировано задач из БД: {Count}", _tasks.Count);
    }

    public async Task AddTask(BotTask task)
    {
        try
        {
            var random = new Random();
            task.AuthViewersCount = (int)(task.MaxViewers * random.Next(60, 81) / 100);
            task.GuestViewersCount = task.MaxViewers - task.AuthViewersCount;

            var isLive = await _twitchService.IsStreamLive(task.ChannelUrl);
            task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Pending;
            task.LastUpdated = DateTime.UtcNow;

            await _taskRepository.AddTask(task);
            _logger.LogInformation("Добавлена новая задача #{Id}, статус: {Status}", task.Id, task.Status);

            if (isLive)
            {
                _ = Task.Run(() => StartTask(task)); // Запускаем асинхронно
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при добавлении задачи");

            task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Failed;
            task.ErrorMessage = ex.Message;
            task.LastUpdated = DateTime.UtcNow;

            await _taskRepository.AddTask(task);
        }
    }

    public async Task StartTask(BotTask task)
    {
        if (_taskTokens.ContainsKey(task.Id))
        {
            _logger.LogWarning("Задача уже запущена: #{Id}", task.Id);
            return;
        }

        // Проверка, что стрим активен
        if (!await _twitchService.IsStreamLive(task.ChannelUrl))
        {
            _logger.LogWarning("Стрим оффлайн: {Url}. Ожидаем запуска.", task.ChannelUrl);
            task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Pending;
            return;
        }

        var cts = new CancellationTokenSource();
        _taskTokens[task.Id] = cts;

        _ = Task.Run(async () =>
        {
            await _semaphore.WaitAsync();

            try
            {
                // Если задача приостановлена, вычисляем оставшееся время
                if (task.Status == TwitchViewerBot.Core.Enums.TaskStatus.Paused)
                {
                    task.StartTime = DateTime.UtcNow - task.ElapsedTime; // Восстанавливаем время начала
                }
                else
                {
                    task.StartTime = DateTime.UtcNow;
                }

                // Статус задачи меняется на Running при успешном подключении ботов
                task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Running;
                task.EndTime = task.StartTime.Value + task.Duration; // Конечное время задачи с учётом паузы

                int durationMinutes = (int)task.Duration.TotalMinutes;

                // Получаем авторизованные аккаунты
                var accounts = await _accountService.GetValidAccounts(task.AuthViewersCount);

                var watchingTasks = new List<Task>();

                // Запускаем ботов для авторизованных пользователей
                foreach (var account in accounts)
                {
                    var proxy = account.Proxy ?? await _proxyService.GetRandomValidProxy();
                    var watchTask = _twitchService.WatchStream(account, proxy, task.ChannelUrl, durationMinutes);
                    watchingTasks.Add(watchTask);
                }

                // Запускаем ботов для гостевых пользователей
                for (int i = 0; i < task.GuestViewersCount; i++)
                {
                    var proxy = await _proxyService.GetRandomValidProxy();
                    var watchTask = _twitchService.WatchAsGuest(proxy, task.ChannelUrl, durationMinutes);
                    watchingTasks.Add(watchTask);
                }

                // Ожидаем выполнения всех задач до окончания времени или отмены
                var taskCompletion = await Task.WhenAny(Task.Delay(task.Duration), Task.WhenAll(watchingTasks));

                if (taskCompletion == await Task.WhenAny(Task.Delay(task.Duration), Task.WhenAll(watchingTasks)))
                {
                    // Задача завершена по времени
                    _logger.LogInformation("Задача #{Id} завершена по времени.", task.Id);
                    task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Completed;
                }
                else
                {
                    // Задача завершена досрочно (например, из-за ошибок или отмены)
                    _logger.LogWarning("Задача #{Id} завершена досрочно.", task.Id);
                    task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Failed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в задаче #{Id}", task.Id);
                task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Failed;
            }
            finally
            {
                _semaphore.Release();
                _taskTokens.Remove(task.Id);
            }

        }, cts.Token);
    }

    public async Task PauseTask(int taskId)
    {
        if (_taskTokens.ContainsKey(taskId))
        {
            var cts = _taskTokens[taskId];
            cts.Cancel(); // Останавливаем текущие операции с ботами
                          // Также обновляем статус задачи на "Paused"
            var task = GetTaskById(taskId); // Получаем задачу по ID
            task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Paused;
            task.ElapsedTime = DateTime.UtcNow - task.StartTime.Value; // Сохраняем время работы задачи
            await _taskRepository.UpdateTask(task); // Обновляем задачу в базе данных
        }
    }
    public async Task ResumeTask(int taskId)
    {
        if (!_taskTokens.ContainsKey(taskId))
        {
            var task = GetTaskById(taskId); // Получаем задачу по ID
            // Возобновляем задачу
            await StartTask(task);
        }
    }
    public BotTask GetTaskById(int taskId)
    {
        // Ищем задачу по ID в коллекции
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);

        if (task == null)
        {
            throw new InvalidOperationException($"Задача с ID {taskId} не найдена.");
        }

        return task;
    }

    public async Task<List<BotTask>> GetAllTasks() => _tasks;

    public async Task<List<BotTask>> GetPendingTasks()
    {
        _logger.LogInformation("Всего задач в памяти: {Count}", _tasks.Count);
        _logger.LogInformation("Статусы: {Statuses}", string.Join(", ", _tasks.Select(t => t.Status)));
        return _tasks.Where(t => t.Status == TwitchViewerBot.Core.Enums.TaskStatus.Pending).ToList();
    }

    public async Task<List<BotTask>> GetRunningTasks() 
    {
        _logger.LogInformation("Всего задач в памяти: {Count}", _tasks.Count);
        _logger.LogInformation("Статусы: {Statuses}", string.Join(", ", _tasks.Select(t => t.Status)));
        return _tasks.Where(t => t.Status == TwitchViewerBot.Core.Enums.TaskStatus.Running).ToList();
    }


    public async Task ProcessPendingTasks()
    {
        var pending = await GetPendingTasks();

        foreach (var task in pending)
        {
            task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Running;
            await StartTask(task);
        }
    }

    public async Task CancelTask(int taskId)
    {
        if (_taskTokens.TryGetValue(taskId, out var cts))
        {
            cts.Cancel();
            _taskTokens.Remove(taskId);
        }

        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task != null) task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Canceled;
    }

    public async Task UpdateTask(BotTask task)
    {
        var index = _tasks.FindIndex(t => t.Id == task.Id);
        if (index != -1)
            _tasks[index] = task;
    }

    public async Task AdjustViewers(BotTask task)
    {
        // можно реализовать позже — увеличивает/уменьшает число потоков
    }

    public async Task CompleteTask(BotTask task)
    {
        task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Completed;
    }

    public async Task<BotTask?> GetById(int id)
    {
        return _tasks.FirstOrDefault(t => t.Id == id);
    }
}
