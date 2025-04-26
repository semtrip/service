// TwitchBot/Core/Services/ProxyService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TwitchViewerBot.Core.Models;
using TwitchViewerBot.Data.Repositories;
using SocksSharp;
using SocksSharp.Proxy;
using Microsoft.EntityFrameworkCore;

namespace TwitchViewerBot.Core.Services
{
    public class ProxyService : IProxyService
    {
        private readonly ILogger<ProxyService> _logger;
        private readonly IProxyRepository _proxyRepository;

        private const string TwitchTestUrl = "https://www.twitch.tv";
        private const int HttpTimeoutSeconds = 15;
        private const int TcpTimeoutMs = 5000;

        public ProxyService(
            ILogger<ProxyService> logger,
            IProxyRepository proxyRepository)
        {
            _logger = logger;
            _proxyRepository = proxyRepository;
        }

        public async Task<int> LoadOrUpdateProxiesFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogError("Proxy file not found: {FilePath}", filePath);
                return 0;
            }

            var lines = await File.ReadAllLinesAsync(filePath);
            var existingProxies = await _proxyRepository.GetAll();
            var existingDict = existingProxies.ToDictionary(p => $"{p.Address}:{p.Port}");

            int changedCount = 0;

            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                try
                {
                    var parts = line.Trim().Split(new[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;

                    var address = parts[0];
                    if (!int.TryParse(parts[1], out var port)) continue;

                    var username = parts.Length > 2 ? parts[2] : string.Empty;
                    var password = parts.Length > 3 ? parts[3] : string.Empty;

                    var key = $"{address}:{port}";
                    if (existingDict.TryGetValue(key, out var existingProxy))
                    {
                        // Обновляем только если изменились учетные данные
                        bool updated = false;

                        if (existingProxy.Username != username)
                        {
                            existingProxy.Username = username;
                            updated = true;
                        }

                        if (existingProxy.Password != password)
                        {
                            existingProxy.Password = password;
                            updated = true;
                        }

                        if (updated)
                        {
                            // Не изменяем IsValid при обновлении
                            await _proxyRepository.UpdateProxy(existingProxy);
                            changedCount++;
                            _logger.LogInformation("Updated proxy credentials for {Proxy}", key);
                        }
                    }
                    else
                    {
                        // Добавляем новый прокси с IsValid = false
                        var proxy = new ProxyServer
                        {
                            Address = address,
                            Port = port,
                            Username = username,
                            Password = password,
                            Type = ProxyType.SOCKS5,
                            IsValid = false, // Только для новых прокси
                            LastChecked = DateTime.MinValue
                        };

                        await _proxyRepository.AddProxy(proxy);
                        changedCount++;
                        _logger.LogInformation("Added new proxy: {Proxy}", key);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing proxy line: {Line}", line);
                }
            }

            return changedCount;
        }

        public async Task<ProxyValidationResult> ValidateProxy(ProxyServer proxy)
        {
            var result = new ProxyValidationResult
            {
                Proxy = proxy,
                TestUrl = TwitchTestUrl
            };

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // 1. Проверка TCP подключения
                if (!await CheckTcpReachability(proxy))
                {
                    return FinalizeResult(result, false, "TCP connection failed", stopwatch);
                }

                // 2. Проверка HTTP/HTTPS через прокси
                using var httpClient = CreateHttpClient(proxy);

                // Тестируем несколько endpoint'ов
                var testUrls = new[]
                {
                    "http://httpbin.org/ip",
                    "https://api.ipify.org",
                    TwitchTestUrl
                };

                foreach (var url in testUrls)
                {
                    try
                    {
                        var response = await httpClient.GetAsync(url);
                        var content = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            if (url == TwitchTestUrl &&
                                !content.Contains("twitch", StringComparison.OrdinalIgnoreCase))
                            {
                                return FinalizeResult(result, false, "Invalid Twitch response", stopwatch);
                            }

                            return FinalizeResult(result, true, null, stopwatch);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Proxy test failed for {Url}", url);
                    }
                }

                return FinalizeResult(result, false, "All test URLs failed", stopwatch);
            }
            catch (Exception ex)
            {
                return FinalizeResult(result, false, GetSimplifiedError(ex), stopwatch);
            }
        }

        public async Task<List<ProxyValidationResult>> ValidateAllProxies()
        {
            var proxies = await _proxyRepository.GetAll();
            var results = new List<ProxyValidationResult>();

            foreach (var proxy in proxies)
            {
                var result = await ValidateProxy(proxy);

                proxy.IsValid = result.IsValid;
                proxy.LastChecked = DateTime.UtcNow;
                await _proxyRepository.UpdateProxy(proxy);

                results.Add(result);

                _logger.LogInformation("Proxy {Address}:{Port} - {Status} - {Error}",
                    proxy.Address, proxy.Port,
                    result.IsValid ? "VALID" : "INVALID",
                    result.ErrorMessage ?? "OK");
            }

            return results;
        }

        public async Task<List<ProxyServer>> GetValidProxies()
        {
            return await _proxyRepository.GetValidProxies();
        }

        private HttpClient CreateHttpClient(ProxyServer proxy)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseProxy = true
            };

            if (proxy.Type == ProxyType.SOCKS5)
            {
                handler.Proxy = new WebProxy($"socks5://{proxy.Address}:{proxy.Port}")
                {
                    Credentials = string.IsNullOrEmpty(proxy.Username)
                        ? null
                        : new NetworkCredential(proxy.Username, proxy.Password)
                };
            }
            else
            {
                handler.Proxy = new WebProxy($"{proxy.Address}:{proxy.Port}")
                {
                    Credentials = string.IsNullOrEmpty(proxy.Username)
                        ? null
                        : new NetworkCredential(proxy.Username, proxy.Password)
                };
            }

            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds),
                DefaultRequestHeaders =
                {
                    {"User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36"},
                    {"Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8"}
                }
            };
        }

        private async Task<bool> CheckTcpReachability(ProxyServer proxy)
        {
            try
            {
                using var tcpClient = new TcpClient();
                var connectTask = tcpClient.ConnectAsync(proxy.Address, proxy.Port);
                var timeoutTask = Task.Delay(TcpTimeoutMs);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                if (completedTask == timeoutTask) return false;

                await connectTask;
                return tcpClient.Connected;
            }
            catch
            {
                return false;
            }
        }

        private string GetSimplifiedError(Exception ex)
        {
            return ex switch
            {
                HttpRequestException hre => hre.StatusCode.HasValue
                    ? $"HTTP error: {(int)hre.StatusCode}"
                    : hre.Message,
                TaskCanceledException => "Request timeout",
                SocketException se => se.SocketErrorCode.ToString(),
                _ => ex.Message
            };
        }

        private ProxyValidationResult FinalizeResult(
            ProxyValidationResult result,
            bool isValid,
            string? errorMessage,
            Stopwatch stopwatch)
        {
            stopwatch.Stop();
            result.IsValid = isValid;
            result.ErrorMessage = errorMessage;
            result.ResponseTime = stopwatch.Elapsed;
            return result;
        }

        
        public async Task AddProxy(ProxyServer proxy) => await _proxyRepository.AddProxy(proxy);
        public async Task UpdateProxy(ProxyServer proxy) => await _proxyRepository.UpdateProxy(proxy);
        public async Task<int> GetProxyCount() => await _proxyRepository.GetCount();
        public async Task<bool> IsProxyValid(ProxyServer proxy)
        {
            try
            {
                var httpClientHandler = new HttpClientHandler
                {
                    Proxy = new WebProxy(proxy.Address, proxy.Port),
                    UseProxy = true
                };

                if (!string.IsNullOrEmpty(proxy.Username) && !string.IsNullOrEmpty(proxy.Password))
                {
                    httpClientHandler.Proxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);
                }

                using var httpClient = new HttpClient(httpClientHandler);
                httpClient.Timeout = TimeSpan.FromSeconds(10); // Устанавливаем тайм-аут

                var response = await httpClient.GetAsync("https://www.twitch.tv");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
        public async Task<ProxyServer> GetRandomValidProxy()
        {
            var valid = await GetValidProxies();
            return valid.OrderBy(_ => Guid.NewGuid()).FirstOrDefault();
        }
    }
}