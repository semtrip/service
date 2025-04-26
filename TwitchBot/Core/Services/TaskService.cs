using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TwitchBot.Core.Models;
using TwitchBot.Data.Repositories;
using TwitchBot.Core.Enums;

namespace TwitchBot.Core.Services
{
    public class TaskService : ITaskService
    {
        private readonly ILogger<TaskService> _logger;
        private readonly ITwitchService _twitchService;
        private readonly IProxyService _proxyService;
        private readonly IAccountService _accountService;
        private readonly ITaskRepository _taskRepository;

        private readonly List<BotTask> _tasks = new();
        private readonly Dictionary<int, CancellationTokenSource> _taskTokens = new();
        private readonly SemaphoreSlim _semaphore = new(20);
        private readonly Random _random = new();
        private readonly object _tasksLock = new();

        public TaskService(
            ILogger<TaskService> logger,
            ITwitchService twitchService,
            IProxyService proxyService,
            IAccountService accountService,
            ITaskRepository taskRepository)
        {
            _logger = logger;
            _twitchService = twitchService;
            _proxyService = proxyService;
            _accountService = accountService;
            _taskRepository = taskRepository;
        }

        public async Task InitializeAsync()
        {
            try
            {
                var activeTasks = await _taskRepository.GetAll();
                activeTasks = activeTasks
                    .Where(t => t.Status == Core.Enums.TaskStatus.Pending ||
                                t.Status == Core.Enums.TaskStatus.Running ||
                                t.Status == Core.Enums.TaskStatus.Paused)
                    .ToList();

                lock (_tasksLock)
                {
                    _tasks.Clear();
                    _tasks.AddRange(activeTasks);
                }

                _logger.LogInformation("Initialized tasks from DB: {Count}", activeTasks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing tasks");
                throw;
            }
        }

        public async Task AddTask(BotTask task)
        {
            try
            {
                task.AuthViewersCount = (int)(task.MaxViewers * _random.Next(60, 81) / 100);
                task.GuestViewersCount = task.MaxViewers - task.AuthViewersCount;
                task.Status = Core.Enums.TaskStatus.Pending;
                task.LastUpdated = DateTime.UtcNow;

                bool isLive = await CheckStreamWithRetry(task.ChannelUrl, 3);

                await _taskRepository.AddTask(task);
                lock (_tasksLock) _tasks.Add(task);

                _logger.LogInformation("Added new task #{Id}, status: {Status}", task.Id, task.Status);

                if (isLive)
                {
                    _ = Task.Run(() => StartTaskWithRetry(task));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding task");

                task.Status = Core.Enums.TaskStatus.Failed;
                task.ErrorMessage = ex.Message;
                task.LastUpdated = DateTime.UtcNow;

                await _taskRepository.AddTask(task);
                throw;
            }
        }

        private async Task<bool> CheckStreamWithRetry(string channelUrl, int maxAttempts)
        {
            int attempts = 0;
            while (attempts < maxAttempts)
            {
                try
                {
                    return await _twitchService.IsStreamLive(channelUrl);
                }
                catch (Exception ex)
                {
                    attempts++;
                    _logger.LogWarning(ex, $"Attempt {attempts} failed for {channelUrl}");
                    if (attempts < maxAttempts)
                    {
                        await Task.Delay(5000 * attempts);
                    }
                }
            }
            return false;
        }

        public async Task StartTask(BotTask task)
        {
            if (_taskTokens.ContainsKey(task.Id))
            {
                _logger.LogWarning("Task already running: #{Id}", task.Id);
                return;
            }

            var cts = new CancellationTokenSource();
            _taskTokens[task.Id] = cts;

            await _semaphore.WaitAsync(cts.Token);
            try
            {
                task.StartTime = DateTime.UtcNow;
                task.Status = Core.Enums.TaskStatus.Running;
                task.EndTime = task.StartTime.Value + task.Duration;
                task.LastUpdated = DateTime.UtcNow;

                await _taskRepository.UpdateTask(task);

                _logger.LogInformation("Starting task #{Id} for {Channel}", task.Id, task.ChannelUrl);

                var accounts = await _accountService.GetValidAccounts(task.AuthViewersCount);
                var proxies = await _proxyService.GetValidProxies();

                if (!proxies.Any())
                {
                    throw new InvalidOperationException("No valid proxies available");
                }

                var watchingTasks = new List<Task>();
                var proxyIndex = 0;

                // Авторизованные зрители
                foreach (var account in accounts)
                {
                    var proxy = account.Proxy ?? proxies[proxyIndex % proxies.Count];
                    watchingTasks.Add(WatchWithAccount(account, proxy, task));
                    proxyIndex++;
                }

                // Гостевые зрители
                for (int i = 0; i < task.GuestViewersCount; i++)
                {
                    var proxy = proxies[proxyIndex % proxies.Count];
                    watchingTasks.Add(WatchAsGuest(proxy, task));
                    proxyIndex++;
                }

                await Task.WhenAny(
                    Task.Delay(task.Duration, cts.Token),
                    Task.WhenAll(watchingTasks));

                if (!cts.Token.IsCancellationRequested)
                {
                    task.Status = Core.Enums.TaskStatus.Completed;
                    _logger.LogInformation("Task #{Id} completed successfully", task.Id);
                }
            }
            catch (Exception ex)
            {
                task.Status = Core.Enums.TaskStatus.Failed;
                task.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error in task #{Id}", task.Id);
            }
            finally
            {
                task.LastUpdated = DateTime.UtcNow;
                await _taskRepository.UpdateTask(task);
                _semaphore.Release();
                _taskTokens.Remove(task.Id);
            }
        }

        private async Task WatchWithAccount(TwitchAccount account, ProxyServer proxy, BotTask task)
        {
            try
            {
                await _twitchService.WatchStream(
                    account,
                    proxy,
                    task.ChannelUrl,
                    (int)task.Duration.TotalMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Account {account.Username} failed in task {task.Id}");
            }
        }

        private async Task WatchAsGuest(ProxyServer proxy, BotTask task)
        {
            try
            {
                await _twitchService.WatchAsGuest(
                    proxy,
                    task.ChannelUrl,
                    (int)task.Duration.TotalMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Guest viewer failed in task {task.Id}");
            }
        }

        private async Task StartTaskWithRetry(BotTask task, int maxRetries = 3)
        {
            int retryCount = 0;
            while (retryCount < maxRetries)
            {
                try
                {
                    await StartTask(task);
                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    _logger.LogWarning(ex, $"Retry {retryCount} for task {task.Id}");

                    if (retryCount < maxRetries)
                    {
                        await Task.Delay(10000 * retryCount);
                    }
                    else
                    {
                        task.Status = Core.Enums.TaskStatus.Failed;
                        task.ErrorMessage = $"Failed after {maxRetries} attempts: {ex.Message}";
                        task.LastUpdated = DateTime.UtcNow;
                        await _taskRepository.UpdateTask(task);
                    }
                }
            }
        }

        public async Task<List<BotTask>> GetAllTasks()
        {
            lock (_tasksLock) return new List<BotTask>(_tasks);
        }

        public async Task<List<BotTask>> GetPendingTasks()
        {
            lock (_tasksLock)
                return _tasks.Where(t => t.Status == Core.Enums.TaskStatus.Pending).ToList();
        }

        public async Task<List<BotTask>> GetRunningTasks()
        {
            lock (_tasksLock)
                return _tasks.Where(t => t.Status == Core.Enums.TaskStatus.Running).ToList();
        }

        public async Task ProcessPendingTasks()
        {
            var pending = await GetPendingTasks();
            foreach (var task in pending)
            {
                _ = Task.Run(() => StartTaskWithRetry(task));
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
            if (task != null)
            {
                task.Status = Core.Enums.TaskStatus.Canceled;
                task.LastUpdated = DateTime.UtcNow;
                await _taskRepository.UpdateTask(task);
            }
        }

        public async Task UpdateTask(BotTask task)
        {
            lock (_tasksLock)
            {
                var index = _tasks.FindIndex(t => t.Id == task.Id);
                if (index != -1)
                    _tasks[index] = task;
            }

            await _taskRepository.UpdateTask(task);
        }

        public async Task<BotTask?> GetById(int id)
        {
            lock (_tasksLock)
                return _tasks.FirstOrDefault(t => t.Id == id);
        }

        public async Task PauseTask(int taskId)
        {
            if (_taskTokens.TryGetValue(taskId, out var cts))
            {
                cts.Cancel();
                _taskTokens.Remove(taskId);
            }

            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                task.Status = Core.Enums.TaskStatus.Paused;
                task.ElapsedTime = DateTime.UtcNow - task.StartTime.Value;
                task.LastUpdated = DateTime.UtcNow;
                await _taskRepository.UpdateTask(task);
            }
        }

        public async Task ResumeTask(int taskId)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null && task.Status == Core.Enums.TaskStatus.Paused)
            {
                _ = Task.Run(() => StartTaskWithRetry(task));
            }
        }

        public async Task CompleteTask(BotTask task)
        {
            task.Status = Core.Enums.TaskStatus.Completed;
            task.LastUpdated = DateTime.UtcNow;
            await _taskRepository.UpdateTask(task);
        }

        public async Task AdjustViewers(BotTask task)
        {
            // Реализация регулировки количества зрителей
            // Можно добавить позже
        }
    }
}