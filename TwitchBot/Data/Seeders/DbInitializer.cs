using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;
using TwitchViewerBot.Core.Services;
using TwitchViewerBot.Data;

namespace TwitchViewerBot.Data.Seeders
{
    public static class DbInitializer
    {
        public static async Task Initialize(AppDbContext context, IProxyService proxyService)
        {
            Console.WriteLine("Инициализация базы данных...");

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var accountsPath = Path.Combine(baseDir, "accounts.txt");
            var proxiesPath = Path.Combine(baseDir, "proxies.txt");

            // Загружаем/обновляем прокси через сервис
            if (!context.Proxies.Any())
            {
                if (File.Exists(proxiesPath))
                {
                    var count = await proxyService.LoadOrUpdateProxiesFromFile(proxiesPath);
                    Console.WriteLine($"Загружено/обновлено {count} прокси");
                }
                else
                {
                    Console.WriteLine("Файл proxies.txt не найден");
                }
            }

            // Загружаем аккаунты как было
            if (!context.Accounts.Any())
            {
                if (File.Exists(accountsPath))
                {
                    var accounts = File.ReadAllLines(accountsPath)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .Select(l => l.Split(' '))
                        .Where(p => p.Length == 2)
                        .Select(p => new TwitchAccount
                        {
                            Username = p[0],
                            AuthToken = p[1],
                            IsValid = true,
                            LastChecked = DateTime.UtcNow
                        }).ToList();

                    await context.Accounts.AddRangeAsync(accounts);
                    Console.WriteLine($"Загружено {accounts.Count} аккаунтов");
                }
                else
                {
                    Console.WriteLine("Файл accounts.txt не найден");
                }
            }

            await context.SaveChangesAsync();
            Console.WriteLine("Инициализация БД завершена");
        }
    }
}