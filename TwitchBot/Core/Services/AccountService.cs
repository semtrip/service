using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TwitchBot.Core.Models;
using TwitchBot.Data.Repositories;
using System.Threading;

namespace TwitchBot.Core.Services
{
    public class AccountService : IAccountService
    {
        private readonly IAccountRepository _accountRepository;
        private readonly IProxyRepository _proxyRepository;
        private readonly IProxyService _proxyService;
        private readonly ITwitchService _twitchService;
        private readonly ILogger<AccountService> _logger;
        private readonly SemaphoreSlim _validationLock = new(1, 1);
        private readonly Random _random = new();

        public AccountService(
            IAccountRepository accountRepository,
            IProxyRepository proxyRepository,
            IProxyService proxyService,
            ITwitchService twitchService,
            ILogger<AccountService> logger)
        {
            _accountRepository = accountRepository;
            _proxyRepository = proxyRepository;
            _proxyService = proxyService;
            _twitchService = twitchService;
            _logger = logger;
        }

        public async Task<List<TwitchAccount>> GetValidAccounts(int count)
        {
            try
            {
                return await _accountRepository.GetValidAccounts(count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting valid accounts");
                return new List<TwitchAccount>();
            }
        }
        public async Task<List<TwitchAccount>> GetRandomValidAccounts(int count)
        {
            try
            {
                return await _accountRepository.GetValidAccounts(count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting valid accounts");
                return new List<TwitchAccount>();
            }
        }

        public async Task UpdateAccount(TwitchAccount account)
        {
            try
            {
                account.LastChecked = DateTime.UtcNow;
                await _accountRepository.UpdateAccount(account);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating account {account.Username}");
                throw;
            }
        }

        public async Task ValidateAccounts()
        {
            await _validationLock.WaitAsync();
            try
            {
                _logger.LogInformation("Starting account validation...");

                var accounts = await _accountRepository.GetAll();
                var proxies = await _proxyService.GetValidProxies();

                if (!proxies.Any())
                {
                    _logger.LogWarning("No valid proxies available for validation");
                    return;
                }

                var validationTasks = new List<Task>();
                var proxyIndex = 0;
                var proxyCount = proxies.Count;

                foreach (var account in accounts)
                {
                    var proxy = proxies[proxyIndex % proxyCount];
                    validationTasks.Add(ValidateAccountWithRetryAsync(account, proxy));

                    proxyIndex++;
                    if (validationTasks.Count >= 5) // Ограничение параллелизма
                    {
                        await Task.WhenAll(validationTasks);
                        validationTasks.Clear();
                        await Task.Delay(5000); // Задержка между группами
                    }
                }

                if (validationTasks.Any())
                {
                    await Task.WhenAll(validationTasks);
                }

                _logger.LogInformation("Account validation completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Account validation failed");
            }
            finally
            {
                _validationLock.Release();
            }
        }

        private async Task ValidateAccountWithRetryAsync(TwitchAccount account, ProxyServer proxy, int maxRetries = 3)
        {
            int retryCount = 0;
            while (retryCount < maxRetries)
            {
                try
                {
                    _logger.LogInformation($"Validation {account.Username}");
                    var isValid = await _twitchService.VerifyAccount(account, proxy);
                    account.IsValid = isValid;
                    account.ProxyId = isValid ? proxy.Id : (int?)null;
                    account.LastChecked = DateTime.UtcNow;
                    if (isValid) {
                        proxy.ActiveAccountsCount++;
                    }

                    await _accountRepository.UpdateAccount(account);
                    await _proxyRepository.UpdateProxy(proxy);

                    _logger.LogInformation($"Account {account.Username} validation: {(isValid ? "VALID" : "INVALID")}");
                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    _logger.LogWarning(ex, $"Retry {retryCount} for account {account.Username}");

                    if (retryCount < maxRetries)
                    {
                        await Task.Delay(3000 * retryCount);
                    }
                    else
                    {
                        account.IsValid = false;
                        account.LastChecked = DateTime.UtcNow;
                        await _accountRepository.UpdateAccount(account);
                        _logger.LogError(ex, $"Validation failed for account {account.Username}");
                    }
                }
            }
        }

        public async Task LoadAccountsFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogError($"File not found: {filePath}");
                return;
            }

            try
            {
                var lines = await File.ReadAllLinesAsync(filePath);
                var newAccounts = new List<TwitchAccount>();
                var existingUsernames = (await _accountRepository.GetAll())
                    .Select(a => a.Username)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    try
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2) continue;

                        var username = parts[0].Trim();
                        var authToken = parts[1].Trim();

                        if (existingUsernames.Contains(username))
                        {
                            _logger.LogDebug($"Account {username} already exists, skipping");
                            continue;
                        }

                        newAccounts.Add(new TwitchAccount
                        {
                            Username = username,
                            AuthToken = authToken,
                            IsValid = false,
                            LastChecked = DateTime.MinValue
                        });

                        existingUsernames.Add(username);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing line: {line}");
                    }
                }

                if (newAccounts.Any())
                {
                    await _accountRepository.AddAccounts(newAccounts);
                    _logger.LogInformation($"Added {newAccounts.Count} new accounts from file");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading accounts from file");
                throw;
            }
        }
    }
}