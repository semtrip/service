using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchViewerBot.ConsoleUI.Commands;

namespace TwitchViewerBot.ConsoleUI.Menus
{
    public class MainMenu
    {
        private readonly Dictionary<string, ICommand> _commands;

        public MainMenu(
            ValidateProxiesCommand validateProxiesCommand,
            ValidateAccountsCommand validateAccountsCommand,
            StartTaskCommand startTaskCommand,
            ShowTasksCommand showTasksCommand,
            ShowLogsCommand showLogsCommand)
        {
            _commands = new Dictionary<string, ICommand>
            {
                ["1"] = validateProxiesCommand,
                ["2"] = validateAccountsCommand,
                ["3"] = startTaskCommand,
                ["4"] = showTasksCommand,
                ["5"] = showLogsCommand
            };
        }

        public async Task ShowAsync()
        {
            while (true)
            {
                Console.Clear();
                PrintMenu();
                var input = Console.ReadLine();

                if (input == "6") break;

                if (_commands.TryGetValue(input, out var command))
                {
                    try
                    {
                        await command.Execute();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                        await Task.Delay(2000);
                    }
                }
                else
                {
                    Console.WriteLine("Invalid command");
                    await Task.Delay(1000);
                }
            }
        }

        private void PrintMenu()
        {
            Console.WriteLine("=== Twitch Viewer Bot ===");
            Console.WriteLine("1. Validate Proxies");
            Console.WriteLine("2. Validate Accounts");
            Console.WriteLine("3. Start New Task");
            Console.WriteLine("4. Show Tasks");
            Console.WriteLine("5. Show Logs");
            Console.WriteLine("6. Exit");
            Console.Write("> ");
        }
    }
}