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
        private readonly ILogger<TaskRunner> _logger;

        public TaskRunner(ITaskService taskService, ILogger<TaskRunner> logger)
        {
            _taskService = taskService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _taskService.ProcessPendingTasks();
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}