using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
            await _taskRepository.AddTask(task);
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
            var pendingTasks = await GetPendingTasks();
            foreach (var task in pendingTasks)
            {
                await StartTask(task);
            }
        }

        public async Task StartTask(BotTask task)
        {
            try
            {
                if (!await _twitchService.IsStreamLive(task.ChannelUrl!))
                {
                    task.Status = Core.Enums.TaskStatus.Paused;
                    await _taskRepository.UpdateTask(task);
                    return;
                }

                var authBots = await _accountService.GetValidAccounts(task.AuthViewersCount);
                var proxies = await _proxyService.GetValidProxies();

                if (authBots.Count == 0 || proxies.Count == 0)
                {
                    _logger.LogWarning($"Not enough resources for task {task.Id}");
                    return;
                }

                var botGroups = authBots
                    .Select((bot, index) => new { Bot = bot, Proxy = proxies[index % proxies.Count] })
                    .GroupBy(x => x.Proxy);

                foreach (var group in botGroups)
                {
                    foreach (var item in group.Take(3))
                    {
                        _ = _twitchService.WatchStream(
                            item.Bot,
                            item.Proxy,
                            task.ChannelUrl!,
                            (int)task.Duration.TotalMinutes);
                    }
                }

                var guestProxies = proxies
                    .Where(p => !authBots.Any(b => b.ProxyId == p.Id))
                    .ToList();

                for (int i = 0; i < task.GuestViewersCount; i++)
                {
                    var proxy = guestProxies[i % guestProxies.Count];
                    _ = _twitchService.WatchAsGuest(proxy, task.ChannelUrl!, (int)task.Duration.TotalMinutes);
                }

                task.Status = Core.Enums.TaskStatus.Running;
                task.StartTime = DateTime.UtcNow;
                task.CurrentViewers = task.MaxViewers;
                await _taskRepository.UpdateTask(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error starting task {task.Id}");
                task.Status = Core.Enums.TaskStatus.Failed;
                await _taskRepository.UpdateTask(task);
            }
        }

        public async Task PauseTask(int taskId)
        {
            var task = await _taskRepository.GetById(taskId);
            if (task != null && task.Status == Core.Enums.TaskStatus.Running)
            {
                task.Status = Core.Enums.TaskStatus.Paused;
                await _taskRepository.UpdateTask(task);
            }
        }

        public async Task ResumeTask(int taskId)
        {
            var task = await _taskRepository.GetById(taskId);
            if (task != null && task.Status == Core.Enums.TaskStatus.Paused)
            {
                task.Status = Core.Enums.TaskStatus.Pending;
                await _taskRepository.UpdateTask(task);
            }
        }

        public async Task CancelTask(int taskId)
        {
            var task = await _taskRepository.GetById(taskId);
            if (task != null)
            {
                task.Status = Core.Enums.TaskStatus.Canceled;
                await _taskRepository.UpdateTask(task);
            }
        }

        public async Task UpdateTask(BotTask task)
        {
            await _taskRepository.UpdateTask(task);
        }
    }
}