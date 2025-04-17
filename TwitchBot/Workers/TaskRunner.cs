using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Services;

namespace TwitchViewerBot.Workers
{
    public class TaskRunner : BackgroundService
    {
        private readonly ITaskService _taskService;
        private readonly TaskMonitor _taskMonitor;
        private readonly ILogger<TaskRunner> _logger;

        public TaskRunner(
            ITaskService taskService,
            TaskMonitor taskMonitor,
            ILogger<TaskRunner> logger)
        {
            _taskService = taskService;
            _taskMonitor = taskMonitor;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("TaskRunner started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Обработка новых задач
                    await _taskService.ProcessPendingTasks();
                    
                    // Мониторинг и корректировка
                    await _taskMonitor.MonitorAndAdjustTasks();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in TaskRunner");
                }

                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}