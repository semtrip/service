using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchBot.ConsoleUI.Helpers;

namespace TwitchBot.ConsoleUI.Commands
{
    public class ShowLogsCommand : ICommand
    {
        private readonly LoggingHelper _loggingHelper;
        private CancellationTokenSource _cts;
        private bool _isLiveMode = false;

        public ShowLogsCommand(LoggingHelper loggingHelper)
        {
            _loggingHelper = loggingHelper;
        }

        public async Task Execute()
        {
            Console.Clear();
            Console.WriteLine("=== УПРАВЛЕНИЕ ЛОГАМИ ===");
            Console.WriteLine("1. Показать исторические логи");
            Console.WriteLine("2. Режим реального времени");
            Console.WriteLine("3. Очистить логи");
            Console.WriteLine("4. Вернуться");
            Console.Write("> ");

            var input = Console.ReadLine();
            switch (input)
            {
                case "1":
                    ShowLogHistory();
                    break;
                case "2":
                    await ShowLiveLogs();
                    break;
                case "3":
                    _loggingHelper.ClearLogs();
                    Console.WriteLine("Логи очищены");
                    await Task.Delay(1000);
                    break;
            }
        }

        private void ShowLogHistory()
        {
            Console.Clear();
            Console.WriteLine("=== ИСТОРИЧЕСКИЕ ЛОГИ ===");
            Console.WriteLine(_loggingHelper.GetRecentLogs());
            Console.WriteLine("\nНажмите любую клавишу...");
            Console.ReadKey();
        }

        private async Task ShowLiveLogs()
        {
            _isLiveMode = true;
            _cts = new CancellationTokenSource();

            Console.Clear();
            Console.WriteLine("=== ЛОГИ В РЕАЛЬНОМ ВРЕМЕНИ ===");
            Console.WriteLine("Нажмите ESC для выхода\n");

            _loggingHelper.LogEntryAdded += OnNewLogEntry;

            try
            {
                while (_isLiveMode && !_cts.Token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                        _isLiveMode = false;

                    await Task.Delay(100);
                }
            }
            finally
            {
                _loggingHelper.LogEntryAdded -= OnNewLogEntry;
                _cts.Dispose();
            }
        }

        private void OnNewLogEntry(string logEntry)
        {
            if (!_isLiveMode) return;

            try
            {
                Console.WriteLine(logEntry);
            }
            catch
            {
                // Игнорируем ошибки вывода
            }
        }
    }
}