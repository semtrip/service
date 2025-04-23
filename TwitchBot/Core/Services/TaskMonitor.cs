using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Core.Services
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
                    await _taskService.ProcessPendingTasks();
                    await _taskService.MonitorActiveTasks();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка в TaskMonitor");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
        }
    }
}