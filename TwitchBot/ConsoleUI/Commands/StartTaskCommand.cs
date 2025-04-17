using System;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;
using TwitchViewerBot.Core.Services;

namespace TwitchViewerBot.ConsoleUI.Commands
{
    public class StartTaskCommand : ICommand
    {
        private readonly ITaskService _taskService;

        public StartTaskCommand(ITaskService taskService)
        {
            _taskService = taskService;
        }

        public async Task Execute()
        {
            try
            {
                Console.WriteLine("=== Создание новой задачи ===");
                
                var task = new BotTask();
                
                Console.Write("URL канала: ");
                var url = Console.ReadLine();
                if (string.IsNullOrEmpty(url))
                    throw new ArgumentException("URL канала не может быть пустым");
                task.ChannelUrl = url;
                
                Console.Write("Пиковый онлайн (10-1000): ");
                var viewersInput = Console.ReadLine();
                if (string.IsNullOrEmpty(viewersInput))
                    throw new ArgumentException("Количество зрителей не может быть пустым");
                task.MaxViewers = Math.Clamp(int.Parse(viewersInput), 10, 1000);
                
                Console.Write("Время взлёта (1-30 мин): ");
                var rampUpInput = Console.ReadLine();
                if (string.IsNullOrEmpty(rampUpInput))
                    throw new ArgumentException("Время взлёта не может быть пустым");
                task.RampUpTime = Math.Clamp(int.Parse(rampUpInput), 1, 30);
                
                Console.Write("Длительность (1-8 часов): ");
                var durationInput = Console.ReadLine();
                if (string.IsNullOrEmpty(durationInput))
                    throw new ArgumentException("Длительность не может быть пустой");
                var hours = Math.Clamp(int.Parse(durationInput), 1, 8);
                task.Duration = TimeSpan.FromHours(hours);
                
                await _taskService.AddTask(task);
                Console.WriteLine($"Задача {task.Id} создана! Авторизованных: {task.AuthViewersCount}, Гостевых: {task.GuestViewersCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
            
            await Task.Delay(1000);
        }
    }
}