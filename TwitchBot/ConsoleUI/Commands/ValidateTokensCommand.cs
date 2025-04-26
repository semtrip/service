using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TwitchBot.ConsoleUI.Helpers;

namespace TwitchBot.ConsoleUI.Commands
{
    public class ValidateTokensCommand : ICommand
    {
        private readonly ILogger<ValidateTokensCommand> _logger;
        private readonly LoggingHelper _loggingHelper;

        public ValidateTokensCommand(
            ILogger<ValidateTokensCommand> logger,
            LoggingHelper loggingHelper)
        {
            _logger = logger;
            _loggingHelper = loggingHelper;
        }

        public async Task Execute()
        {
            Console.WriteLine("=== Twitch Token Validator ===");
            
            // Выбор файла с токенами
            Console.WriteLine("Введите путь к файлу с токенами (формат: логин токен):");
            var tokensFilePath = Console.ReadLine();
            
            if (!File.Exists(tokensFilePath))
            {
                Console.WriteLine("Файл не найден!");
                return;
            }

            // Выбор файла для валидных токенов
            Console.WriteLine("Введите путь для сохранения валидных токенов:");
            var validTokensFilePath = Console.ReadLine();

            try
            {
                var lines = await File.ReadAllLinesAsync(tokensFilePath);
                var tokenEntries = lines
                    .Select(line => line.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries))
                    .Where(parts => parts.Length == 2)
                    .Select(parts => new { Login = parts[0], Token = parts[1] })
                    .ToArray();

                Console.WriteLine($"Найдено {tokenEntries.Length} токенов для проверки");

                var results = new ConcurrentBag<(string Login, string Token, bool IsValid)>();
                using var httpClient = new HttpClient();

                // Настройка параллельной проверки
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 10 };

                await Parallel.ForEachAsync(tokenEntries, parallelOptions, async (entry, ct) =>
                {
                    var isValid = await ValidateToken(entry.Token, httpClient);
                    results.Add((entry.Login, entry.Token, isValid));
                    
                    Console.ForegroundColor = isValid ? ConsoleColor.Green : ConsoleColor.Red;
                    Console.WriteLine($"{entry.Login}: {entry.Token} - {(isValid ? "VALID" : "INVALID")}");
                    Console.ResetColor();
                });

                // Сохранение валидных токенов в исходном формате
                var validEntries = results.Where(r => r.IsValid);
                await File.WriteAllLinesAsync(
                    validTokensFilePath, 
                    validEntries.Select(e => $"{e.Login} {e.Token}"));

                // Статистика
                Console.WriteLine("\n=== Результаты проверки ===");
                Console.WriteLine($"Всего записей: {tokenEntries.Length}");
                Console.WriteLine($"Валидных: {validEntries.Count()}");
                Console.WriteLine($"Невалидных: {tokenEntries.Length - validEntries.Count()}");
                Console.WriteLine($"\nВалидные токены сохранены в: {validTokensFilePath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке токенов");
                Console.WriteLine($"Произошла ошибка: {ex.Message}");
            }

            Console.WriteLine("\nНажмите любую клавишу для продолжения...");
            Console.ReadKey();
        }

        private async Task<bool> ValidateToken(string token, HttpClient httpClient)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get, 
                    "https://id.twitch.tv/oauth2/validate");
                
                request.Headers.Add("Authorization", $"Bearer {token}");
                
                using var response = await httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}