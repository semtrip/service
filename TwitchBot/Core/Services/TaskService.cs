using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Enums;
using TwitchViewerBot.Core.Models;
using TwitchViewerBot.Data.Repositories;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TwitchViewerBot.Core.Enums;
using TwitchViewerBot.Core.Models;
using TwitchViewerBot.Data.Repositories;

namespace TwitchViewerBot.Core.Services
{
    public class TaskService : ITaskService
    {
        private readonly ITaskRepository _taskRepository;
        private readonly ITwitchService _twitchService;
        private readonly IAccountService _accountService;
        private readonly IProxyService _proxyService;
        private readonly ILogger<TaskService> _logger;
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _activeTasks = new();


        public TaskService(
            ITaskRepository taskRepository,
            ITwitchService twitchService,
            IAccountService accountService,
            IProxyService proxyService,
            ILogger<TaskService> logger)
        {
            _taskRepository = taskRepository;
            _twitchService = twitchService;
            _accountService = accountService;
            _proxyService = proxyService;
            _logger = logger;
        }

        public async Task AddTask(BotTask task)
        {
            try
            {
                // Рассчитываем количество авторизованных зрителей (60-80% от общего числа)
                var random = new Random();
                var authPercentage = random.Next(60, 81) / 100.0;
                task.AuthViewersCount = (int)Math.Round(task.MaxViewers * authPercentage);
                task.GuestViewersCount = task.MaxViewers - task.AuthViewersCount;

                // Проверка канала и стрима
                var isLive = await _twitchService.IsStreamLive(task.ChannelUrl);

                if (!isLive)
                {
                    task.Status = Core.Enums.TaskStatus.Paused;
                }
                else
                {
                    task.Status = Core.Enums.TaskStatus.Running;
                    task.StartTime = DateTime.UtcNow;
                    task.EndTime = DateTime.UtcNow.Add(task.Duration);
                }

                await _taskRepository.AddTask(task);

                if (task.Status == Core.Enums.TaskStatus.Running)
                {
                    await StartTask(task);
                }
            }
            catch (Exception ex)
            {
                task.Status = Core.Enums.TaskStatus.Failed;
                task.ErrorMessage = $"Ошибка при создании задачи: {ex.Message}";
                await _taskRepository.AddTask(task);
                _logger.LogError(ex, "Ошибка при добавлении задачи");
            }
        }

        public async Task StartTask(BotTask task)
        {
            var isLive = await _twitchService.IsStreamLive(task.ChannelUrl);
            if (!isLive)
            {
                Console.WriteLine($"{task.ChannelUrl} стрим не запущен, ставим на паузу");
                task.Status = Core.Enums.TaskStatus.Paused;
                await _taskRepository.UpdateTask(task);
                return;
            }

            task.Status = Core.Enums.TaskStatus.Running;
            task.StartTime = DateTime.UtcNow;
            task.EndTime = DateTime.UtcNow.Add(task.Duration);
            Console.WriteLine($"{task.ChannelUrl} стрим запущен, начинаем накрутку");

            await _taskRepository.UpdateTask(task);
            await DistributeViewers(task);
        }

        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(10, 10);

        public async Task DistributeViewers(BotTask task)
        {
            try
            {
                _logger.LogInformation($"Starting distribution for task {task.Id}: {task.AuthViewersCount} auth, {task.GuestViewersCount} guest viewers");

                // Получаем аккаунты и прокси параллельно
                var getAuthTask = GetAuthAccounts(task.AuthViewersCount);
                var getGuestTask = GetGuestProxies(task.GuestViewersCount);
                await Task.WhenAll(getAuthTask, getGuestTask);

                var authAccounts = await getAuthTask;
                var guestProxies = await getGuestTask;

                _logger.LogInformation($"Retrieved {authAccounts.Count} auth accounts and {guestProxies.Count} proxies");

                // Список всех задач для отслеживания
                var allTasks = new List<Task>();

                // Запуск авторизованных зрителей с ограничением параллелизма
                var authTasks = authAccounts.Select(async (pair) =>
                {
                    await _semaphore.WaitAsync();
                    try
                    {
                        var (account, proxy) = pair;
                        _logger.LogInformation($"Starting auth viewer: {account.Username} via {proxy.Address}");
                        await _twitchService.WatchStream(account, proxy, task.ChannelUrl, (int)task.Duration.TotalMinutes);
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                });
                allTasks.AddRange(authTasks);

                // Запуск гостевых зрителей с ограничением параллелизма
                var guestTasks = guestProxies.Select(async proxy =>
                {
                    await _semaphore.WaitAsync();
                    try
                    {
                        _logger.LogInformation($"Starting guest viewer via {proxy.Address}");
                        await _twitchService.WatchAsGuest(proxy, task.ChannelUrl, (int)task.Duration.TotalMinutes);
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                });
                allTasks.AddRange(guestTasks);

                // Обновляем счетчики после запуска
                task.CurrentViewers = authAccounts.Count + guestProxies.Count;
                await _taskRepository.UpdateTask(task);

                // Ждем завершения всех задач с таймаутом
                var timeout = TimeSpan.FromMinutes(5);
                var completedTask = await Task.WhenAny(
                    Task.WhenAll(allTasks),
                    Task.Delay(timeout)
                );

                if (completedTask is Task delayTask && delayTask.IsCompleted)
                {
                    _logger.LogWarning($"Not all viewers started within {timeout.TotalMinutes} minutes");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in DistributeViewers for task {task.Id}");
            }
        }

        private async Task<List<(TwitchAccount, ProxyServer)>> GetAuthAccounts(int count)
        {
            if (count <= 0) return new List<(TwitchAccount, ProxyServer)>();

            // Используем параллелизм для проверки аккаунтов
            var accounts = (await _accountService.GetValidAccounts(count))
                .AsParallel()
                .Where(a => a.Proxy != null || _proxyService.GetValidProxies().Result.Any())
                .Take(count)
                .ToList();

            var result = new ConcurrentBag<(TwitchAccount, ProxyServer)>();

            Parallel.ForEach(accounts, account =>
            {
                var proxy = account.Proxy ?? _proxyService.GetValidProxies().Result.First();
                result.Add((account, proxy));
            });

            return result.ToList();
        }

        private async Task<List<ProxyServer>> GetGuestProxies(int count)
        {
            if (count <= 0) return new List<ProxyServer>();

            return (await _proxyService.GetValidProxies())
                .AsParallel()
                .Take(count)
                .ToList();
        }


        private async Task ExecuteTaskAsync(BotTask task, CancellationToken cancellationToken)
        {
            try
            {
                // Подключаем ботов с плавным набором
                await RampUpBots(task, cancellationToken);

                // Основной цикл выполнения задачи
                while (!cancellationToken.IsCancellationRequested && !task.IsExpired)
                {
                    await AdjustViewers(task);
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                    task.LastUpdated = DateTime.UtcNow;
                    await _taskRepository.UpdateTask(task);
                }

                // Завершение задачи
                await CompleteTask(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при выполнении задачи {task.Id}");
                task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Failed;
                task.ErrorMessage = ex.Message;
                await _taskRepository.UpdateTask(task);
            }
        }

        private async Task RampUpBots(BotTask task, CancellationToken ct)
        {
            int current = 0;
            while (current < task.MaxViewers && !ct.IsCancellationRequested)
            {
                int toAdd = Math.Min(task.ViewersPerMinute, task.MaxViewers - current);
                await AddBotsToStream(task, toAdd);
                current += toAdd;
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
            }
        }

        private async Task AddBotsToStream(BotTask task, int count)
        {
            var accounts = await _accountService.GetValidAccounts(count);
            var proxies = await _proxyService.GetValidProxies();

            foreach (var (account, proxy) in accounts.Zip(proxies, (a, p) => (a, p)))
            {
                _ = _twitchService.WatchStream(account, proxy, task.ChannelUrl, (int)task.Duration.TotalMinutes);
                account.LastChecked = DateTime.UtcNow;
                await _accountService.UpdateAccount(account);
                await _proxyService.UpdateProxy(proxy);
            }

            task.CurrentViewers += count;
        }

        public async Task AdjustViewers(BotTask task)
        {
            // Проверяем онлайн стрима
            bool isLive = await _twitchService.IsStreamLive(task.ChannelUrl);

            if (!isLive)
            {
                task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Paused;
                return;
            }

            // Корректируем количество зрителей (+/- 15%)
            int target = task.MaxViewers;
            int min = (int)(target * 0.85);
            int max = (int)(target * 1.15);

            if (task.CurrentViewers < min)
            {
                int toAdd = min - task.CurrentViewers;
                await AddBotsToStream(task, toAdd);
            }
            else if (task.CurrentViewers > max)
            {
                int toRemove = task.CurrentViewers - max;
                await RemoveBotsFromStream(task, toRemove);
            }
        }

        private async Task RemoveBotsFromStream(BotTask task, int count)
        {
            // Логика удаления ботов
            task.CurrentViewers -= count;
        }

        public async Task CompleteTask(BotTask task)
        {
            try
            {
                // Останавливаем всех ботов
                await RemoveAllBots(task);

                // Обновляем статус задачи
                task.Status =  TwitchViewerBot.Core.Enums.TaskStatus.Completed;
                task.CompletedTime = DateTime.UtcNow;
                await _taskRepository.UpdateTask(task);

                _logger.LogInformation($"Задача {task.Id} завершена");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при завершении задачи {task.Id}");
                throw;
            }
        }

        private async Task RemoveAllBots(BotTask task)
        {
            // Логика удаления всех ботов
            task.CurrentViewers = 0;
            await _taskRepository.UpdateTask(task);
        }

        public async Task<List<BotTask>> GetAllTasks()
        {
            return await _taskRepository.GetAll();
        }

        public async Task<List<BotTask>> GetPendingTasks()
        {
            return await _taskRepository.GetPendingTasks();
        }

        public async Task<List<BotTask>> GetRunningTasks()
        {
            return await _taskRepository.GetRunningTasks();
        }

        public async Task ProcessPendingTasks()
        {
            var pendingTasks = await _taskRepository.GetPendingTasks();
            foreach (var task in pendingTasks)
            {
                await StartTask(task);
            }
        }

        public async Task PauseTask(int taskId)
        {
            if (_activeTasks.TryGetValue(taskId, out var cts))
            {
                cts.Cancel();
                var task = await _taskRepository.GetById(taskId);
                if (task != null)
                {
                    task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Paused;
                    await _taskRepository.UpdateTask(task);
                }
            }
        }

        public async Task ResumeTask(int taskId)
        {
            var task = await _taskRepository.GetById(taskId);
            if (task != null && task.Status == TwitchViewerBot.Core.Enums.TaskStatus.Paused)
            {
                await StartTask(task);
            }
        }

        public async Task CancelTask(int taskId)
        {
            if (_activeTasks.TryGetValue(taskId, out var cts))
            {
                cts.Cancel();
                var task = await _taskRepository.GetById(taskId);
                if (task != null)
                {
                    task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Canceled;
                    await _taskRepository.UpdateTask(task);
                }
            }
        }

        public async Task UpdateTask(BotTask task)
        {
            await _taskRepository.UpdateTask(task);
        }
        public async Task<BotTask?> GetById(int id)
        {
            return await _taskRepository.GetById(id);
        }
    }

}