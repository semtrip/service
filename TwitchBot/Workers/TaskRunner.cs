using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TwitchViewerBot.Core.Services;

namespace TwitchViewerBot.Workers
{
    public class TaskRunner : BackgroundService
    {
        private readonly ILogger<TaskRunner> _logger;
        private readonly ITaskService _taskService;
        private readonly TaskMonitor _taskMonitor;

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
            _logger.LogInformation("TaskRunner запущен");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogDebug("Проверка задач...");

                    var peddingTasks = await _taskService.GetPendingTasks();
                    _logger.LogInformation($"Найдено {peddingTasks.Count} задач в ожидании запуска");

                    foreach (var task in peddingTasks)
                    {
                        _logger.LogInformation($"Запуск задачи {task.Id} для {task.ChannelUrl}");
                        _logger.LogInformation($"Запрошено просмотров: Авторизованных {task.AuthViewersCount} Не авторизованных {task.GuestViewersCount}");
                        await _taskService.StartTask(task);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка в TaskRunner");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("TaskRunner остановлен");
        }
    }

}