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
                // Проверка канала и стрима
                var isLive = await _twitchService.IsStreamLive(task.ChannelUrl);

                if (!isLive)
                {
                    task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Paused;
                }
                else
                {
                    task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Running;
                    task.StartTime = DateTime.UtcNow;
                    task.EndTime = DateTime.UtcNow.Add(task.Duration);
                }

                await _taskRepository.AddTask(task);

                if (task.Status == TwitchViewerBot.Core.Enums.TaskStatus.Running)
                {
                    await StartTask(task);
                }
            }
            catch (Exception ex)
            {
                task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Failed;
                task.ErrorMessage = $"Ошибка при создании задачи: {ex.Message}";
                await _taskRepository.AddTask(task);
                _logger.LogError(ex, "Ошибка при добавлении задачи");
            }
        }

        public async Task StartTask(BotTask task)
        {
            var cts = new CancellationTokenSource();
            _activeTasks.TryAdd(task.Id, cts);

            _ = Task.Run(async () => await ExecuteTaskAsync(task, cts.Token), cts.Token);
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

        private async Task RemoveAllBots(BotTask task)
        {
            // Логика удаления всех ботов
            task.CurrentViewers = 0;
        }

        private async Task CompleteTask(BotTask task)
        {
            await RemoveAllBots(task);
            task.Status = TwitchViewerBot.Core.Enums.TaskStatus.Completed;
            task.EndTime = DateTime.UtcNow;
            await _taskRepository.UpdateTask(task);
            _activeTasks.TryRemove(task.Id, out _);
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
    }

}