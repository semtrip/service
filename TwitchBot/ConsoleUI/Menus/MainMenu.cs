using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchViewerBot.ConsoleUI.Commands;

namespace TwitchViewerBot.ConsoleUI.Menus
{
    public class MainMenu
    {
        private readonly Dictionary<string, ICommand> _commands;
        private readonly Dictionary<string, string> _menuItems;

        public MainMenu(
            ValidateProxiesCommand validateProxiesCommand,
            ValidateAccountsCommand validateAccountsCommand,
            StartTaskCommand startTaskCommand,
            ValidateTokensCommand validateTokensCommand,
            ShowTasksCommand showTasksCommand,
            ShowLogsCommand showLogsCommand)
        {
            _commands = new Dictionary<string, ICommand>
            {
                ["1"] = validateProxiesCommand,
                ["2"] = validateAccountsCommand,
                ["3"] = validateTokensCommand,
                ["4"] = startTaskCommand,
                ["5"] = showTasksCommand,
                ["6"] = showLogsCommand
            };

            _menuItems = new Dictionary<string, string>
            {
                ["1"] = "Проверить прокси",
                ["2"] = "Проверить аккаунты",
                ["3"] = "Проверить токены",
                ["4"] = "Создать задачу",
                ["5"] = "Список задач",
                ["6"] = "Показать логи",
                ["7"] = "Выход"
            };
        }

        public async Task ShowAsync()
        {
            while (true)
            {
                try
                {
                    Console.Clear();
                    PrintMenu();

                    // Используем Console.ReadLine() вместо Console.ReadKey()
                    var input = Console.ReadLine();

                    if (string.IsNullOrEmpty(input))
                        continue;

                    if (input == "7") break;

                    if (_commands.TryGetValue(input, out var command))
                    {
                        try
                        {
                            await command.Execute();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Ошибка: {ex.Message}");
                            await WaitForAnyKey();
                        }
                    }
                    else
                    {
                        Console.WriteLine("Неверная команда");
                        await Task.Delay(1000);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Критическая ошибка: {ex.Message}");
                    await WaitForAnyKey();
                }
            }
        }

        private async Task WaitForAnyKey()
        {
            try
            {
                Console.WriteLine("Нажмите Enter для продолжения...");
                // Используем ReadLine() вместо ReadKey()
                while (Console.ReadKey(true).Key != ConsoleKey.Enter) { }
            }
            catch (InvalidOperationException)
            {
                // Если консольный ввод недоступен, просто ждем 2 секунды
                await Task.Delay(2000);
            }
        }

        private void PrintMenu()
        {
            Console.WriteLine("=== Twitch Viewer Bot ===");
            foreach (var item in _menuItems)
            {
                Console.WriteLine($"{item.Key}. {item.Value}");
            }
            Console.Write("> ");
        }
    }
}