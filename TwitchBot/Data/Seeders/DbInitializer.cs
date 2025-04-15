using Microsoft.EntityFrameworkCore;
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

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var accountsPath = Path.Combine(baseDir, "accounts.txt");
            var proxiesPath = Path.Combine(baseDir, "proxies.txt");

            // Загрузка аккаунтов
            if (!await context.Accounts.AnyAsync())
            {

                if (File.Exists(accountsPath))
                {
                    try
                    {
                        var accountLines = File.ReadAllLines(accountsPath);
                        Console.WriteLine($"Найдено {accountLines.Length} строк в файле");

                        var accounts = accountLines
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .Select(l => l.Split(' '))
                            .Where(p => p.Length == 2)
                            .Select(p => new TwitchAccount
                            {
                                Username = p[0],
                                AuthToken = p[1],
                                IsValid = true,
                                LastChecked = DateTime.Now
                            }).ToList();

                        await context.Accounts.AddRangeAsync(accounts);
                    }
                    catch (Exception ex)
                    {
                    }
                }
                else
                {
                    Console.WriteLine("Файл accounts.txt не найден, пропускаю загрузку аккаунтов");
                }
            }
            else
            {
                Console.WriteLine("\nТаблица Accounts уже содержит данные, пропускаю загрузку");
            }

            // Загрузка прокси
            if (!await context.Proxies.AnyAsync())
            {
                Console.WriteLine("\nТаблица Proxies пустая, начинаю загрузку...");

                if (File.Exists(proxiesPath))
                {
                    try
                    {
                        var proxyLines = File.ReadAllLines(proxiesPath);
                        Console.WriteLine($"Найдено {proxyLines.Length} строк в файле");

                        var proxies = proxyLines
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .Select(l => l.Split('@'))
                            .Where(p => p.Length == 2)
                            .Select(p => {
                                var hostParts = p[0].Split(':');
                                var authParts = p[1].Split(':');
                                return new ProxyServer
                                {
                                    Address = hostParts[0],
                                    Port = hostParts.Length > 1 ? int.Parse(hostParts[1]) : 8080, // Значение по умолчанию
                                    Username = authParts[0],
                                    Password = authParts.Length > 1 ? authParts[1] : string.Empty,
                                    IsValid = true,
                                    LastChecked = DateTime.Now
                                };
                            }).ToList();

                        Console.WriteLine($"Добавлено {proxies.Count} прокси");
                        await context.Proxies.AddRangeAsync(proxies);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка загрузки прокси: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("Файл proxies.txt не найден, пропускаю загрузку прокси");
                }
            }
            else
            {
                Console.WriteLine("\nТаблица Proxies уже содержит данные, пропускаю загрузку");
            }

            try
            {
                var changes = await context.SaveChangesAsync();
                Console.WriteLine($"\nСохранено изменений в БД: {changes}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nОшибка сохранения в БД: {ex.Message}");
            }

            Console.WriteLine("\n=== ЗАВЕРШЕНИЕ ИНИЦИАЛИЗАЦИИ БД ===");
        }
    }
}