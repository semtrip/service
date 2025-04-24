using System;
using System.Collections.Generic;
using System.Linq;
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
                var random = new Random();
                task.AuthViewersCount = (int)(task.MaxViewers * random.Next(60, 81) / 100);
                task.GuestViewersCount = task.MaxViewers - task.AuthViewersCount;

                var isLive = await _twitchService.IsStreamLive(task.ChannelUrl);
                task.Status = isLive ? Core.Enums.TaskStatus.Running : Core.Enums.TaskStatus.Pending;

                await _taskRepository.AddTask(task);

                if (isLive)
                {
                    _ = Task.Run(() => StartTask(task)); // Запускаем в фоне
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding task");
                task.Status = Core.Enums.TaskStatus.Failed;
                await _taskRepository.AddTask(task);
            }
        }

        public async Task StartTask(BotTask task)
        {
            try
            {
                task.Status = Core.Enums.TaskStatus.Running;
                await _taskRepository.UpdateTask(task);

                await DistributeViewers(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error starting task {task.Id}");
                task.Status = Core.Enums.TaskStatus.Failed;
                await _taskRepository.UpdateTask(task);
            }
        }

        private async Task DistributeViewers(BotTask task)
        {
            var authAccounts = await GetAuthAccounts(task.AuthViewersCount);
            var guestProxies = await GetGuestProxies(task.GuestViewersCount);
            _logger.LogInformation($"Получено ботов: Авторизованных {authAccounts.Count} Не авторизованных {guestProxies.Count}");
            // Запускаем авторизованных зрителей
            var authTasks = authAccounts.Select(pair =>
                _twitchService.WatchStream(pair.account, pair.proxy, task.ChannelUrl, (int)task.Duration.TotalMinutes));

            // Запускаем гостевых зрителей
            var guestTasks = guestProxies.Select(proxy =>
                _twitchService.WatchAsGuest(proxy, task.ChannelUrl, (int)task.Duration.TotalMinutes));

            await Task.WhenAll(authTasks.Concat(guestTasks));

            task.CurrentViewers = authAccounts.Count + guestProxies.Count;
            await _taskRepository.UpdateTask(task);
        }

        private async Task<List<(TwitchAccount account, ProxyServer proxy)>> GetAuthAccounts(int count)
        {
            var accounts = (await _accountService.GetValidAccounts(count))
                .Where(a => a.Proxy != null)
                .Take(count)
                .ToList();

            var proxies = await _proxyService.GetValidProxies();
            return accounts.Select(a => (a, a.Proxy ?? proxies[new Random().Next(proxies.Count)])).ToList();
        }

        private async Task<List<ProxyServer>> GetGuestProxies(int count)
        {
            return (await _proxyService.GetValidProxies()).Take(count).ToList();
        }

        // Остальные методы интерфейса
        public async Task<List<BotTask>> GetAllTasks() => await _taskRepository.GetAll();
        public async Task<List<BotTask>> GetPendingTasks() => await _taskRepository.GetPendingTasks();
        public async Task<List<BotTask>> GetRunningTasks() => await _taskRepository.GetRunningTasks();
        public async Task ProcessPendingTasks()
        {
            var tasks = await _taskRepository.GetPendingTasks();
            foreach (var task in tasks) await StartTask(task);
        }
        public async Task PauseTask(int taskId) => await UpdateTaskStatus(taskId, Core.Enums.TaskStatus.Paused);
        public async Task ResumeTask(int taskId) => await UpdateTaskStatus(taskId, Core.Enums.TaskStatus.Running);
        public async Task CancelTask(int taskId) => await UpdateTaskStatus(taskId, Core.Enums.TaskStatus.Canceled);
        public async Task UpdateTask(BotTask task) => await _taskRepository.UpdateTask(task);
        public async Task<BotTask?> GetById(int id) => await _taskRepository.GetById(id);
        public async Task CompleteTask(BotTask task) => await UpdateTaskStatus(task.Id, Core.Enums.TaskStatus.Completed);
        public async Task AdjustViewers(BotTask task)
        {
            // Реализация корректировки зрителей
            await Task.CompletedTask;
        }

        private async Task UpdateTaskStatus(int taskId, Core.Enums.TaskStatus status)
        {
            var task = await _taskRepository.GetById(taskId);
            if (task != null)
            {
                task.Status = status;
                await _taskRepository.UpdateTask(task);
            }
        }
    }
}