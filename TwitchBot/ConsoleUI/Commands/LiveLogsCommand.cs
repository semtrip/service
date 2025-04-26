using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchBot.ConsoleUI.Helpers;

namespace TwitchBot.ConsoleUI.Commands
{
    public class LiveLogsCommand : ICommand
    {
        private CancellationTokenSource _cts;
        private bool _isActive;
        private const int MaxDisplayLines = 20;
        private readonly FixedSizeQueue<string> _lastLogs = new FixedSizeQueue<string>(MaxDisplayLines);

        public async Task Execute()
        {
            _isActive = true;
            _cts = new CancellationTokenSource();

            Console.Clear();
            Console.WriteLine("=== ЛОГИ В РЕАЛЬНОМ ВРЕМЕНИ ===");
            Console.WriteLine("Нажмите ESC для выхода\n");
            Console.WriteLine("Последние события:");

            // Подписываемся на новые логи
            AdvancedLogger.LogAdded += HandleNewLog;

            // Основной цикл
            while (_isActive && !_cts.Token.IsCancellationRequested)
            {
                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                    _isActive = false;

                await Task.Delay(100);
            }

            AdvancedLogger.LogAdded -= HandleNewLog;
            _cts.Dispose();
        }

        private void HandleNewLog(string log)
        {
            if (!_isActive) return;

            _lastLogs.Enqueue(log);

            Console.SetCursorPosition(0, 4); // Позиция для вывода логов
            foreach (var line in _lastLogs)
            {
                Console.WriteLine(line.PadRight(Console.WindowWidth - 1).Substring(0, Console.WindowWidth - 1));
            }
        }
    }

    public class FixedSizeQueue<T> : Queue<T>
    {
        private readonly int _maxSize;
        public FixedSizeQueue(int maxSize) => _maxSize = maxSize;

        public new void Enqueue(T item)
        {
            base.Enqueue(item);
            while (Count > _maxSize)
                Dequeue();
        }
    }
}