using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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
        private readonly ILogger<TaskService> _logger;

        public TaskService(
            ITaskRepository taskRepository,
            ITwitchService twitchService,
            ILogger<TaskService> logger)
        {
            _taskRepository = taskRepository;
            _twitchService = twitchService;
            _logger = logger;
        }

        public async Task ProcessPendingTasks()
        {
            var pendingTasks = await _taskRepository.GetPendingTasks();
            foreach (var task in pendingTasks)
            {
                await StartTask(task);
            }
        }

        public async Task StartTask(BotTask task)
        {
            try
            {
                task.Status = Core.Enums.TaskStatus.Running;
                task.StartTime = DateTime.Now;
                await _taskRepository.UpdateTask(task);

                await RunBotsForTask(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error starting task {task.Id}");
                task.Status = Core.Enums.TaskStatus.Failed;
                await _taskRepository.UpdateTask(task);
            }
        }

        private async Task RunBotsForTask(BotTask task)
        {
            // Реализация запуска ботов
        }

        public async Task PauseTask(int taskId)
        {
            var task = await _taskRepository.GetById(taskId);
            if (task != null)
            {
                task.Status = Core.Enums.TaskStatus.Paused;
                await _taskRepository.UpdateTask(task);
            }
        }

        public async Task ResumeTask(int taskId)
        {
            var task = await _taskRepository.GetById(taskId);
            if (task != null)
            {
                task.Status = Core.Enums.TaskStatus.Running;
                await _taskRepository.UpdateTask(task);
            }
        }

        public async Task<List<BotTask>> GetAllTasks()
        {
            return await _taskRepository.GetAll();
        }

        public async Task AddTask(BotTask task)
        {
            await _taskRepository.AddTask(task);
        }
    }
}