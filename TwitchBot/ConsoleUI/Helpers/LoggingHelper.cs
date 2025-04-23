using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace TwitchViewerBot.ConsoleUI.Helpers
{
    public class LoggingHelper : ILogger
    {
        private readonly string _logFilePath;
        private readonly BlockingCollection<string> _logQueue = new BlockingCollection<string>();
        private readonly Thread _loggingThread;

        public event Action<string> LogEntryAdded;

        public LoggingHelper()
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, $"bot_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            _loggingThread = new Thread(ProcessLogQueue)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _loggingThread.Start();
        }

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state,
                      Exception exception, Func<TState, Exception, string> formatter)
        {
            // Используем переданный formatter для преобразования состояния в строку
            var message = formatter(state, exception);
            var formattedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {message}";

            _logQueue.Add(formattedMessage);
            LogEntryAdded?.Invoke(formattedMessage);
        }

        private void ProcessLogQueue()
        {
            foreach (var message in _logQueue.GetConsumingEnumerable())
            {
                try
                {
                    File.AppendAllText(_logFilePath, message + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка записи лога: {ex.Message}");
                }
            }
        }

        public string GetRecentLogs(int maxLines = 100)
        {
            try
            {
                if (!File.Exists(_logFilePath)) return "Логи отсутствуют";

                var lines = File.ReadAllLines(_logFilePath);
                var start = Math.Max(0, lines.Length - maxLines);
                return string.Join(Environment.NewLine, lines, start, lines.Length - start);
            }
            catch (Exception ex)
            {
                return $"Ошибка чтения логов: {ex.Message}";
            }
        }

        public void ClearLogs()
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    File.Delete(_logFilePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка очистки логов: {ex.Message}");
            }
        }

        public IDisposable BeginScope<TState>(TState state) => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
    }
}