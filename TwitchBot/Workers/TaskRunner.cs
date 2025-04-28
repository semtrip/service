using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TwitchBot.Core.Services;
using TwitchBot.Core.Services;

namespace TwitchBot.Workers
{
    public class TaskRunner : BackgroundService
    {
        private readonly ILogger<TaskRunner> _logger;
        private readonly ITaskService _taskService;
        private readonly WebDriverPool _driverPool;

        public TaskRunner(
            ITaskService taskService,
            WebDriverPool driverPool,
            ILogger<TaskRunner> logger)
        {
            _taskService = taskService;
            _driverPool = driverPool;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("TaskRunner started");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var pendingTasks = await _taskService.GetPendingTasks();
                    foreach (var task in pendingTasks)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        _logger.LogInformation($"Starting task {task.Id} for {task.ChannelUrl}");
                        _ = _taskService.StartTask(task);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Error in TaskRunner");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("TaskRunner stopped");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _taskService.CancelAllTasks();
            _driverPool.Dispose();
            await base.StopAsync(cancellationToken);
        }
    }
}