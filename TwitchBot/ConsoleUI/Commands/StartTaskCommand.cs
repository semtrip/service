using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TwitchBot.Core.Models;
using TwitchBot.Core.Services;

namespace TwitchBot.ConsoleUI.Commands
{
    public class StartTaskCommand : ICommand
    {
        private readonly ITaskService _taskService;
        private readonly ILogger<StartTaskCommand> _logger;

        public StartTaskCommand(
            ITaskService taskService,
            ILogger<StartTaskCommand> logger)
        {
            _taskService = taskService;
            _logger = logger;
        }

        public async Task Execute()
        {
            try
            {
                Console.Clear();
                Console.WriteLine("=== Создание новой задачи ===");

                var task = await CollectTaskData();
                await _taskService.AddTask(task);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\nЗадача {task.Id} успешно создана!");
                Console.WriteLine($"Авторизованных зрителей: {task.AuthViewersCount}");
                Console.WriteLine($"Гостевых зрителей: {task.GuestViewersCount}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nОшибка: {ex.Message}");
                Console.ResetColor();
                _logger.LogError(ex, "Ошибка при создании задачи");
            }
            finally
            {
                Console.WriteLine("\nНажмите Enter для продолжения...");
                while (Console.KeyAvailable) Console.ReadKey(true); // Очистка буфера
                Console.ReadLine();
            }
        }

        private async Task<BotTask> CollectTaskData()
        {
            var task = new BotTask();

            Console.Write("\nURL канала (например: https://www.twitch.tv/username): ");
            task.ChannelUrl = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(task.ChannelUrl))
                throw new ArgumentException("URL канала не может быть пустым");

            Console.Write("Пиковый онлайн (10-1000): ");
            if (!int.TryParse(Console.ReadLine(), out var maxViewers) || maxViewers < 10 || maxViewers > 1000)
                throw new ArgumentException("Некорректное количество зрителей (должно быть 10-1000)");
            task.MaxViewers = maxViewers;

            Console.Write("Время взлета (1-30 мин): ");
            if (!int.TryParse(Console.ReadLine(), out var rampUp) || rampUp < 1 || rampUp > 30)
                throw new ArgumentException("Некорректное время набора (должно быть 1-30 минут)");
            task.RampUpTime = rampUp;

            Console.Write("Длительность (1-8 часов): ");
            if (!int.TryParse(Console.ReadLine(), out var hours) || hours < 1 || hours > 8)
                throw new ArgumentException("Некорректная длительность (должно быть 1-8 часов)");
            task.Duration = TimeSpan.FromHours(hours);

            return task;
        }
    }
}