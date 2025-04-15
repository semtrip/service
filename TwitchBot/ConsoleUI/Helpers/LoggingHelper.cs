using System.IO;

namespace TwitchViewerBot.ConsoleUI.Helpers
{
    public class LoggingHelper
    {
        private readonly string _logFilePath = "bot_log.txt";

        public string GetLogs()
        {
            return File.Exists(_logFilePath) ? File.ReadAllText(_logFilePath) : "No logs available";
        }

        public void Log(string message)
        {
            File.AppendAllText(_logFilePath, $"[{DateTime.Now}] {message}{Environment.NewLine}");
        }
    }
}