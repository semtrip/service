using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TwitchViewerBot.ConsoleUI.Helpers;
using TwitchViewerBot.Core.Services;

namespace TwitchViewerBot.ConsoleUI.Commands
{
    public class ValidateAccountsCommand : ICommand
    {
        private readonly IAccountService _accountService;
        private readonly ILogger<ValidateAccountsCommand> _logger;

        public ValidateAccountsCommand(
            IAccountService accountService,
            ILogger<ValidateAccountsCommand> logger)
        {
            _accountService = accountService;
            _logger = logger;
        }

        public async Task Execute()
        {
            Console.WriteLine("Начало проверки аккаунтов...");

            try
            {
                await _accountService.ValidateAccounts();
                Console.WriteLine("Проверка аккаунтов завершена.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке аккаунтов");
                Console.WriteLine("Ошибка при проверке аккаунтов.");
            }

            Console.WriteLine("Нажмите любую клавишу для продолжения...");
            Console.ReadKey();
        }
    }

}