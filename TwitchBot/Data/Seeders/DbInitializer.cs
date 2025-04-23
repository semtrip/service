using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TwitchViewerBot.Core.Models;
using TwitchViewerBot.Core.Services;
using TwitchViewerBot.Data;

namespace TwitchViewerBot.Data.Seeders
{
    public class DbInitializer
    {
        public static async Task Initialize(AppDbContext context, IProxyService proxyService, ILogger<DbInitializer> logger)
        {
            logger.LogInformation("Инициализация базы данных...");

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var accountsPath = Path.Combine(baseDir, "accounts.txt");
            var proxiesPath = Path.Combine(baseDir, "proxies.txt");

            try
            {
                await LoadProxiesAsync(context, proxiesPath, proxyService, logger);
                await LoadAccountsAsync(context, accountsPath, logger);

                await context.SaveChangesAsync(); // Сохраняем изменения в базе данных
                logger.LogInformation("Инициализация БД завершена.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка при инициализации базы данных");
                throw;
            }
        }

        private static async Task LoadProxiesAsync(AppDbContext context, string proxiesPath, IProxyService proxyService, ILogger<DbInitializer> logger)
        {
            if (!await context.Proxies.AnyAsync())
            {
                if (File.Exists(proxiesPath))
                {
                    var count = await proxyService.LoadOrUpdateProxiesFromFile(proxiesPath);
                    logger.LogInformation($"Загружено/обновлено {count} прокси.");
                }
                else
                {
                    logger.LogWarning("Файл proxies.txt не найден.");
                }
            }
            else
            {
                logger.LogInformation("Прокси уже загружены.");
            }
        }

        private static async Task LoadAccountsAsync(AppDbContext context, string accountsPath, ILogger<DbInitializer> logger)
        {
            if (!await context.Accounts.AnyAsync())
            {
                if (File.Exists(accountsPath))
                {
                    var accounts = (await File.ReadAllLinesAsync(accountsPath))
                        .Where(l => !string.IsNullOrWhiteSpace(l)) // Пропуск пустых строк
                        .Select(l => l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) // Разделение по пробелам
                        .Where(p => p.Length == 2) // Проверка на два элемента (username и authToken)
                        .Select(p => new TwitchAccount
                        {
                            Username = p[0],
                            AuthToken = p[1],
                            IsValid = false, // По умолчанию аккаунт не проверен
                            LastChecked = DateTime.MinValue // Дата последней проверки
                        })
                        .ToList();

                    await context.Accounts.AddRangeAsync(accounts);
                    logger.LogInformation($"Загружено {accounts.Count} аккаунтов.");
                }
                else
                {
                    logger.LogWarning("Файл accounts.txt не найден.");
                }
            }
            else
            {
                logger.LogInformation("Аккаунты уже загружены.");
            }
        }
    }
}
