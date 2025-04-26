using Microsoft.Extensions.Logging;

namespace TwitchBot.ConsoleUI.Helpers
{
    public class AdvancedLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new AdvancedLogger(categoryName);
        }

        public void Dispose()
        {
            // Очистка ресурсов
        }
    }
}