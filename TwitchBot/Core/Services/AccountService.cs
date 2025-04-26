using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;
using TwitchViewerBot.Data.Repositories;
using System.Threading;

namespace TwitchViewerBot.Core.Services
{
    public class AccountService : IAccountService
    {
        private readonly IAccountRepository _accountRepository;
        private readonly IProxyService _proxyService;
        private readonly ITwitchService _twitchService;
        private readonly ILogger<AccountService> _logger;

        public AccountService(
            IAccountRepository accountRepository,
            IProxyService proxyService,
            ITwitchService twitchService,
            ILogger<AccountService> logger)
        {
            _accountRepository = accountRepository;
            _proxyService = proxyService;
            _twitchService = twitchService;
            _logger = logger;
        }

        private static readonly SemaphoreSlim _proxySemaphore = new SemaphoreSlim(1, 1); // Асинхронная блокировка

        public async Task<List<TwitchAccount>> GetValidAccounts(int count)
        {
            return await _accountRepository.GetValidAccounts(count);
        }

        public async Task UpdateAccount(TwitchAccount account)
        {
            await _accountRepository.UpdateAccount(account);
        }

        public async Task ValidateAccounts()
        {
            var accounts = await _accountRepository.GetAll(); // Получаем все аккаунты.
            var proxies = await _proxyService.GetValidProxies(); // Получаем все валидные прокси.

            // Параллельно валидируем аккаунты, но с ограничением на количество параллельных задач.
            var tasks = accounts.Select(account => ValidateAccountAsync(account, proxies)).ToList();
            await Task.WhenAll(tasks);
        }

        private async Task ValidateAccountAsync(TwitchAccount account, List<ProxyServer> proxies)
        {
            try
            {
                ProxyServer proxy = null;

                // Синхронизируем доступ к прокси
                await _proxySemaphore.WaitAsync();
                try
                {
                    // Фильтруем прокси, которые еще не достигли лимита
                    var availableProxies = proxies
                        .Where(p => p.ActiveAccountsCount < 3) // Лимит: 3 аккаунта на прокси
                        .OrderBy(p => p.ActiveAccountsCount) // Выбираем прокси с наименьшим количеством аккаунтов
                        .ToList();

                    if (availableProxies.Count == 0)
                    {
                        _logger.LogWarning($"Нет доступных прокси для проверки аккаунта {account.Username}");
                        return;
                    }

                    // Перебираем доступные прокси, пока не найдем валидный
                    foreach (var availableProxy in availableProxies)
                    {
                        proxy = availableProxy;
                        proxy.ActiveAccountsCount++; // Увеличиваем счетчик активных аккаунтов для прокси
                        _logger.LogInformation($"Прокси {proxy.Address}:{proxy.Port} выбран для аккаунта {account.Username}");
                        break;
                    }

                    if (proxy == null)
                    {
                        _logger.LogWarning($"Нет валидных прокси для проверки аккаунта {account.Username}");
                        return;
                    }
                }
                finally
                {
                    _proxySemaphore.Release();
                }

                var isValid = await _twitchService.VerifyAccount(account, proxy);

                account.IsValid = isValid;
                account.ProxyId = isValid ? proxy.Id : (int?)null;
                account.LastChecked = DateTime.UtcNow;

                await _accountRepository.UpdateAccount(account);

                _logger.LogInformation($"Аккаунт {account.Username} проверен: {(isValid ? "валиден" : "невалиден")}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при проверке аккаунта {account.Username}");

                // Уменьшаем счетчик активных аккаунтов для прокси в случае ошибки
                await _proxySemaphore.WaitAsync();
                try
                {
                    var proxy = proxies.FirstOrDefault(p => p.Id == account.ProxyId);
                    if (proxy != null)
                    {
                        proxy.ActiveAccountsCount--;
                    }
                }
                finally
                {
                    _proxySemaphore.Release();
                }
            }
        }

        public async Task LoadAccountsFromFile(string filePath) // Добавлено
        {
            if (!File.Exists(filePath))
            {
                _logger.LogError($"Файл {filePath} не найден.");
                return;
            }

            var lines = await File.ReadAllLinesAsync(filePath);
            foreach (var line in lines)
            {
                try
                {
                    var parts = line.Split(':');
                    if (parts.Length != 2)
                    {
                        _logger.LogWarning($"Некорректный формат строки: {line}");
                        continue;
                    }

                    var username = parts[0].Trim();
                    var authToken = parts[1].Trim();

                    var account = new TwitchAccount
                    {
                        Username = username,
                        AuthToken = authToken,
                        IsValid = false,
                        LastChecked = DateTime.MinValue
                    };

                    await _accountRepository.AddAccount(account);
                    _logger.LogInformation($"Аккаунт {username} загружен из файла.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Ошибка при обработке строки: {line}");
                }
            }
        }
    }
}
