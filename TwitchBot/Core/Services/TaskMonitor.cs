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

        // В TaskMonitor.cs
        public async Task MonitorAndAdjustTasks()
        {
            var runningTasks = await _taskService.GetRunningTasks();

            foreach (var task in runningTasks)
            {
                try
                {
                    // Проверяем, жив ли стрим
                    var isLive = await _twitchService.IsStreamLive(task.ChannelUrl);
                    if (!isLive)
                    {
                        await _taskService.PauseTask(task.Id);
                        continue;
                    }

                    // Имитация естественного колебания зрителей
                    var fluctuation = CalculateViewerFluctuation(task);
                    task.CurrentViewers = Math.Clamp(
                        task.CurrentViewers + fluctuation,
                        (int)(task.MaxViewers * 0.7), // Минимум 70% от максимума
                        task.MaxViewers);

                    task.LastUpdated = DateTime.UtcNow;
                    await _taskService.UpdateTask(task);

                    _logger.LogInformation($"Adjusted viewers for task {task.Id}: {task.CurrentViewers}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error monitoring task {task.Id}");
                }
            }
        }

        private int CalculateViewerFluctuation(BotTask task)
        {
            // Более сложная логика колебаний
            var baseFluctuation = (int)(task.MaxViewers * 0.1);
            var randomFactor = _random.NextDouble() * 0.3 - 0.15; // -15% to +15%
            return (int)(baseFluctuation * (1 + randomFactor));
        }
    }
}