using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using TwitchBot.Core.Models;
using TwitchBot.Core.Services;
using TwitchBot.Data.Repositories;
using TwitchBot.Core.Enums;
using TwitchBot.Core.Models;
using TwitchBot.Data.Repositories;
using ICSharpCode.SharpZipLib.Zip;

namespace TwitchBot.Core.Services
{
    public class TaskService : ITaskService
    {
        private readonly ILogger<TaskService> _logger;
        private readonly ITwitchService _twitchService;
        private readonly IProxyService _proxyService;
        private readonly IAccountService _accountService;
        private readonly ITaskRepository _taskRepository;
        private readonly WebDriverPool _driverPool;

        private readonly List<BotTask> _tasks = new();
        private readonly Dictionary<int, CancellationTokenSource> _taskTokens = new();

        public TaskService(
            ILogger<TaskService> logger,
            ITwitchService twitchService,
            IProxyService proxyService,
            IAccountService accountService,
            ITaskRepository taskRepository,
            WebDriverPool driverPool)
        {
            _logger = logger;
            _twitchService = twitchService;
            _proxyService = proxyService;
            _accountService = accountService;
            _taskRepository = taskRepository;
            _driverPool = driverPool;
        }

        public async Task InitializeAsync()
        {
            var activeTasks = await _taskRepository.GetAll();
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
                task.Status = Core.Enums.TaskStatus.Pending;
                task.LastUpdated = DateTime.UtcNow;
                task.ElapsedTime = task.Duration;
                await _taskRepository.AddTask(task);
                _logger.LogInformation($"Добавлена новая задача #{task.Id}, статус: {task.Status}");

                if (isLive)
                {
                    _ = Task.Run(() => StartTask(task));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении задачи");
                task.Status = Core.Enums.TaskStatus.Failed;
                task.ErrorMessage = ex.Message;
                task.LastUpdated = DateTime.UtcNow;
                await _taskRepository.AddTask(task);
            }
        }

        public async Task StartTask(BotTask task)
        {
            if (_taskTokens.ContainsKey(task.Id)) return;
            var isLive = await _twitchService.IsStreamLive(task.ChannelUrl);

            if (isLive)
            {
                var cts = new CancellationTokenSource();
                _taskTokens[task.Id] = cts;
                var drivers = new List<(IWebDriver driver, ProxyServer proxy)>();

                try
                {
                    _logger.LogInformation($"Начинаю выполненеи задачи #{task.Id} запрошенно просмотров {task.MaxViewers}");
                    task.StartTime = DateTime.UtcNow;
                    task.EndTime = task.StartTime.Value + task.ElapsedTime;
                    task.Status = Core.Enums.TaskStatus.Running;
                    await _taskRepository.UpdateTask(task);
                    var watchingTasks = new List<Task>();

                    // Получаем валидные аккаунты с привязанными прокси
                    var accounts = await _accountService.GetValidAccounts(task.AuthViewersCount);
                    _logger.LogInformation($"Для задачи #{task.Id} полученно {accounts.Count} аккаунтов");
                    foreach (var account in accounts)
                    {
                        if (account.Proxy == null || !account.Proxy.IsValid)
                        {
                            _logger.LogWarning($"Аккаунт {account.Username} не имеет валидного прокси, пропускаем");
                            continue;
                        }

                        var driver = await _driverPool.GetDriver(account.Proxy, cts.Token);
                        drivers.Add((driver, account.Proxy));
                        watchingTasks.Add(WatchAccountWithRetry(driver, account, account.Proxy, task, cts.Token));
                    }

                    // Гостевые просмотры через случайные валидные прокси
                    for (int i = 0; i < task.GuestViewersCount; i++)
                    {
                        var proxy = await _proxyService.GetRandomValidProxy();
                        if (proxy == null)
                        {
                            _logger.LogWarning("Не найдены валидные прокси для гостевого просмотра");
                            continue;
                        }

                        var driver = await _driverPool.GetDriver(proxy, cts.Token);
                        drivers.Add((driver, proxy));
                        watchingTasks.Add(WatchGuestWithRetry(driver, proxy, task, cts.Token));
                    }

                    await Task.WhenAny(
                        Task.Delay(task.Duration, cts.Token),
                        Task.WhenAll(watchingTasks)
                    );

                    task.Status = cts.IsCancellationRequested
                        ? Core.Enums.TaskStatus.Canceled
                        : Core.Enums.TaskStatus.Completed;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    task.Status = Core.Enums.TaskStatus.Failed;
                    _logger.LogError(ex, $"Ошибка в задаче #{task.Id}");
                }
                finally
                {
                    foreach (var (driver, proxy) in drivers)
                    {
                        _driverPool.ReleaseDriver(driver, proxy);
                    }

                    _taskTokens.Remove(task.Id);
                    await _taskRepository.UpdateTask(task);
                }
            }
            else 
            {
                 _logger.LogInformation($"Стрим оффлайн ${task.ChannelUrl}");
            }        
        }

        private async Task WatchAccountWithRetry(
            IWebDriver driver,
            TwitchAccount account,
            ProxyServer proxy,
            BotTask task,
            CancellationToken ct)
        {
            try
            {
                await _twitchService.WatchStream(driver, account, proxy, task.ChannelUrl, (int)task.Duration.TotalMinutes);
                //await _twitchService.WatchLightweight(account, proxy, task.ChannelUrl, (int)task.Duration.TotalMinutes);
            }
            catch
            {
                if (!ct.IsCancellationRequested)
                {
                    await Task.Delay(5000, ct);
                    await WatchAccountWithRetry(driver, account, proxy, task, ct);
                }
            }
        }

        private async Task WatchGuestWithRetry(
            IWebDriver driver,
            ProxyServer proxy,
            BotTask task,
            CancellationToken ct)
        {
            try
            {
                //await _twitchService.WatchLightweight(null, proxy, task.ChannelUrl, (int)task.Duration.TotalMinutes);
                await _twitchService.WatchAsGuest(driver, proxy, task.ChannelUrl, (int)task.Duration.TotalMinutes);
            }
            catch
            {
                if (!ct.IsCancellationRequested)
                {
                    await Task.Delay(5000, ct);
                    await WatchGuestWithRetry(driver, proxy, task, ct);
                }
            }
        }

        public async Task<List<BotTask>> GetAllTasks()
        {
            return _tasks;
        }

        public async Task<List<BotTask>> GetPendingTasks()
        {
            return _tasks.Where(t => t.Status == Core.Enums.TaskStatus.Pending).ToList();
        }

        public async Task<List<BotTask>> GetRunningTasks()
        {
            return _tasks.Where(t => t.Status == Core.Enums.TaskStatus.Running).ToList();
        }

        public async Task ProcessPendingTasks()
        {
            var pending = await GetPendingTasks();
            foreach (var task in pending)
            {
                task.Status = Core.Enums.TaskStatus.Running;
                await StartTask(task);
            }
        }

        public async Task PauseTask(int taskId)
        {
            if (_taskTokens.TryGetValue(taskId, out var cts))
            {
                cts.Cancel();
                var task = GetTaskById(taskId);
                task.Status = Core.Enums.TaskStatus.Paused;
                task.ElapsedTime = DateTime.UtcNow - task.StartTime.Value;
                await _taskRepository.UpdateTask(task);
            }
        }

        public async Task ResumeTask(int taskId)
        {
            if (!_taskTokens.ContainsKey(taskId))
            {
                var task = GetTaskById(taskId);
                await StartTask(task);
            }
        }

        public BotTask GetTaskById(int taskId)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) throw new InvalidOperationException($"Задача с ID {taskId} не найдена.");
            return task;
        }

        public async Task CancelTask(int taskId)
        {
            if (_taskTokens.TryGetValue(taskId, out var cts))
            {
                cts.Cancel();
                _taskTokens.Remove(taskId);
            }

            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null) task.Status = Core.Enums.TaskStatus.Canceled;
        }

        public async Task UpdateTask(BotTask task)
        {
            var index = _tasks.FindIndex(t => t.Id == task.Id);
            if (index != -1) _tasks[index] = task;
        }

        public async Task AdjustViewers(BotTask task)
        {
            // Реализация может быть добавлена позже
        }

        public async Task CompleteTask(BotTask task)
        {
            task.Status = Core.Enums.TaskStatus.Completed;
        }

        public async Task<BotTask?> GetById(int id)
        {
            return _tasks.FirstOrDefault(t => t.Id == id);
        }

        public async Task CancelAllTasks()
        {
            foreach (var cts in _taskTokens.Values)
            {
                cts.Cancel();
            }
            _taskTokens.Clear();
        }
    }
}