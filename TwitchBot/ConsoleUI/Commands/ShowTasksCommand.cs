using System;
using System.Threading.Tasks;
using TwitchBot.Core.Models;
using TwitchBot.Core.Services;

namespace TwitchBot.ConsoleUI.Commands
{
    public class ShowTasksCommand : ICommand
    {
        private readonly ITaskService _taskService;

        public ShowTasksCommand(ITaskService taskService)
        {
            _taskService = taskService;
        }

        public async Task Execute()
        {
            var tasks = await _taskService.GetAllTasks();
            Console.WriteLine("=== Active Tasks ===");
            foreach (var task in tasks)
            {
                Console.WriteLine($"ID: {task.Id} | Status: {task.Status} | Viewers: {task.CurrentViewers}/{task.MaxViewers} | Channel: {task.ChannelUrl}");
            }
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
    }
}