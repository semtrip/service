using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TwitchViewerBot.ConsoleUI.Helpers
{
    public class AdvancedLogger : ILogger
    {
        private readonly string _categoryName;
        private static readonly BlockingCollection<string> _logQueue = new BlockingCollection<string>();
        private static readonly SemaphoreSlim _logSemaphore = new SemaphoreSlim(1, 1);
        private static string _logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        private static string _currentLogFile = Path.Combine(_logDirectory, $"bot_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        public static event Action<string> LogAdded;

        static AdvancedLogger()
        {
            Directory.CreateDirectory(_logDirectory);
            Task.Run(ProcessLogQueue);
        }

        public AdvancedLogger(string categoryName)
        {
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var message = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {_categoryName}: {formatter(state, exception)}";
            _logQueue.Add(message);
            LogAdded?.Invoke(message);
        }

        private static async Task ProcessLogQueue()
        {
            foreach (var message in _logQueue.GetConsumingEnumerable())
            {
                await _logSemaphore.WaitAsync();
                try
                {
                    await File.AppendAllTextAsync(_currentLogFile, message + Environment.NewLine);
                }
                finally
                {
                    _logSemaphore.Release();
                }
            }
        }

        public static List<string> GetLogFiles()
        {
            return Directory.Exists(_logDirectory)
                ? new List<string>(Directory.GetFiles(_logDirectory, "bot_*.log").OrderByDescending(f => f))
                : new List<string>();
        }

        public static string ReadLogFile(string filePath, int maxLines = 200)
        {
            if (!File.Exists(filePath)) return "Файл не найден";

            var lines = File.ReadLines(filePath).TakeLast(maxLines);
            return string.Join(Environment.NewLine, lines);
        }
    }
}