using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TwitchBot.Core.Models;

namespace TwitchBot.Core.Services
{
    public class TaskMonitor : BackgroundService
    {
        private readonly ITaskService _taskService;
        private readonly ILogger<TaskMonitor> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

        public TaskMonitor(ITaskService taskService, ILogger<TaskMonitor> logger)
        {
            _taskService = taskService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var runningTasks = await _taskService.GetRunningTasks();
                    foreach (var task in runningTasks)
                    {
                        if (task.EndTime <= DateTime.UtcNow)
                        {
                            await _taskService.CompleteTask(task);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка в TaskMonitor");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task MonitorAndAdjustTasks()
        {
            var runningTasks = await _taskService.GetRunningTasks();
            foreach (var task in runningTasks)
            {
                try
                {
                    await _taskService.AdjustViewers(task);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Ошибка при мониторинге задачи {task.Id}");
                }
            }
        }

    }

}