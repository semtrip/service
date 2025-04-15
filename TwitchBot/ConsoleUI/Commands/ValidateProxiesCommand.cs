using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;
using TwitchViewerBot.Core.Services;
using TwitchViewerBot.ConsoleUI.Helpers;

namespace TwitchViewerBot.ConsoleUI.Commands
{
    public class ValidateProxiesCommand : ICommand
    {
        private readonly IProxyService _proxyService;
        private readonly ILogger<ValidateProxiesCommand> _logger;

        public ValidateProxiesCommand(IProxyService proxyService, ILogger<ValidateProxiesCommand> logger)
        {
            _proxyService = proxyService;
            _logger = logger;
        }

        public async Task Execute()
        {
            try
            {
                Console.WriteLine("Starting proxy validation...");
                var results = await _proxyService.ValidateAllProxies();

                ConsoleHelper.PrintProxyValidationResults(results);

                var validCount = results.Count(r => r.IsValid);
                Console.WriteLine($"\nValidation completed. Valid: {validCount}, Invalid: {results.Count - validCount}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Proxy validation failed");
                Console.WriteLine("Error during proxy validation");
            }

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
    }
}