using System;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Enums;
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
            Console.Write("Enter channel URL: ");
            var channelUrl = Console.ReadLine();

            Console.Write("Enter max viewers (10-1000): ");
            if (!int.TryParse(Console.ReadLine(), out var maxViewers) || maxViewers < 10 || maxViewers > 1000)
            {
                Console.WriteLine("Invalid viewers count");
                return;
            }

            var task = new BotTask
            {
                ChannelUrl = channelUrl,
                MaxViewers = maxViewers,
                Status = Core.Enums.TaskStatus.Pending
            };

            await _taskService.AddTask(task);
            Console.WriteLine($"Task {task.Id} created");
            await Task.Delay(1000);
        }
    }
}