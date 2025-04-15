using System.Threading.Tasks;
using TwitchViewerBot.ConsoleUI.Helpers;

namespace TwitchViewerBot.ConsoleUI.Commands
{
    public class ShowLogsCommand : ICommand
    {
        private readonly LoggingHelper _loggingHelper;

        public ShowLogsCommand(LoggingHelper loggingHelper)
        {
            _loggingHelper = loggingHelper;
        }

        public async Task Execute()
        {
            Console.WriteLine("=== Logs ===");
            var logs = _loggingHelper.GetLogs();
            Console.WriteLine(logs);
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
            await Task.CompletedTask;
        }
    }
}