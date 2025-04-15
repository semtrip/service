using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TwitchViewerBot.ConsoleUI.Helpers;
using TwitchViewerBot.Core.Services;

namespace TwitchViewerBot.ConsoleUI.Commands
{
    public class ValidateAccountsCommand : ICommand
    {
        private readonly ITwitchService _twitchService;
        private readonly IProxyService _proxyService;
        private readonly ILogger<ValidateAccountsCommand> _logger;

        public ValidateAccountsCommand(
            ITwitchService twitchService,
            IProxyService proxyService,
            ILogger<ValidateAccountsCommand> logger)
        {
            _twitchService = twitchService;
            _proxyService = proxyService;
            _logger = logger;
        }

        public async Task Execute()
        {
            Console.WriteLine("Starting accounts validation...");
            // Реализация проверки аккаунтов
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
            await Task.CompletedTask;
        }
    }
}