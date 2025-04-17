using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Core.Services
{
    public class TaskMonitor
    {
        private readonly ITaskService _taskService;
        private readonly ITwitchService _twitchService;
        private readonly ILogger<TaskMonitor> _logger;
        private readonly Random _random;

        public TaskMonitor(
            ITaskService taskService,
            ITwitchService twitchService,
            ILogger<TaskMonitor> logger)
        {
            _taskService = taskService;
            _twitchService = twitchService;
            _logger = logger;
            _random = new Random();
        }

        public async Task MonitorAndAdjustTasks()
        {
            var runningTasks = (await _taskService.GetRunningTasks())
                .Where(t => t.LastUpdated < DateTime.UtcNow.AddMinutes(-5))
                .ToList();

            foreach (var task in runningTasks)
            {
                try
                {
                    if (!await _twitchService.IsStreamLive(task.ChannelUrl!))
                    {
                        await _taskService.PauseTask(task.Id);
                        continue;
                    }

                    var fluctuation = (int)(task.MaxViewers * 0.25 * _random.NextDouble());
                    var direction = _random.Next(2) == 0 ? -1 : 1;
                    var newViewers = task.CurrentViewers + (direction * fluctuation);

                    task.CurrentViewers = Math.Clamp(
                        newViewers,
                        (int)(task.MaxViewers * 0.75),
                        task.MaxViewers);

                    task.LastUpdated = DateTime.UtcNow;
                    await _taskService.UpdateTask(task);

                    _logger.LogInformation($"Adjusted viewers for task {task.Id} to {task.CurrentViewers}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error monitoring task {task.Id}");
                }
            }
        }
    }
}