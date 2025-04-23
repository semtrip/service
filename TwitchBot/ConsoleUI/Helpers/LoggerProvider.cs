using Microsoft.Extensions.Logging;
using System;

namespace TwitchViewerBot.ConsoleUI.Helpers
{
    public class LoggerProvider : ILoggerProvider
    {
        private readonly LoggingHelper _loggingHelper;

        public LoggerProvider(LoggingHelper loggingHelper)
        {
            _loggingHelper = loggingHelper;
        }

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
        {
            return new CategoryLogger(categoryName, _loggingHelper);
        }

        public void Dispose()
        {
            // Очистка ресурсов
        }

        private class CategoryLogger : Microsoft.Extensions.Logging.ILogger
        {
            private readonly string _categoryName;
            private readonly LoggingHelper _loggingHelper;

            public CategoryLogger(string categoryName, LoggingHelper loggingHelper)
            {
                _categoryName = categoryName;
                _loggingHelper = loggingHelper;
            }

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state,
                                  Exception exception, Func<TState, Exception, string> formatter)
            {
                // Создаем новый formatter, который включает имя категории
                Func<TState, Exception, string> wrappedFormatter = (s, e) =>
                    $"[{_categoryName}] {formatter(s, e)}";

                // Передаем оригинальный state и новый formatter
                _loggingHelper.Log(logLevel, eventId, state, exception, wrappedFormatter);
            }
        }
    }
}