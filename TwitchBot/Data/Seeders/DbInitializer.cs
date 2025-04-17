using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;
using TwitchViewerBot.Data;

namespace TwitchViewerBot.Data.Seeders
{
    public static class DbInitializer
    {
        public static async Task Initialize(BotDbContext context)
        {
            Console.WriteLine("Инициализация базы данных...");

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var accountsPath = Path.Combine(baseDir, "accounts.txt");
            var proxiesPath = Path.Combine(baseDir, "proxies.txt");

            // Load accounts
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

            // Load proxies
            if (!context.Proxies.Any())
            {
                if (File.Exists(proxiesPath))
                {
                    var proxies = File.ReadAllLines(proxiesPath)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .Select(l => l.Split(':'))
                        .Where(p => p.Length >= 2)
                        .Select(p => new ProxyServer
                        {
                            Address = p[0],
                            Port = int.Parse(p[1]),
                            Username = p.Length > 2 ? p[2] : "",
                            Password = p.Length > 3 ? p[3] : "",
                            IsValid = false,
                            LastChecked = DateTime.MinValue
                        }).ToList();

                    await context.Proxies.AddRangeAsync(proxies);
                    Console.WriteLine($"Загружено {proxies.Count} прокси");
                }
                else
                {
                    Console.WriteLine("Файл proxies.txt не найден");
                }
            }

            await context.SaveChangesAsync();
            Console.WriteLine("Инициализация БД завершена");
        }
    }
}