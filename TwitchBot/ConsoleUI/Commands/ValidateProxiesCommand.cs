using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using TwitchBot.Core.Models;
using TwitchBot.Core.Services;
using TwitchBot.ConsoleUI.Helpers;

namespace TwitchBot.ConsoleUI.Commands
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
                _logger.LogInformation("Starting proxy validation...");
                var results = await _proxyService.ValidateAllProxies();
                var validCount = results.Count(r => r.IsValid);
                _logger.LogInformation($"\nValidation completed. Valid: {validCount}, Invalid: {results.Count - validCount}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Proxy validation failed");
            }
        }
    }
}