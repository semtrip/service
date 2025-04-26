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
using TwitchBot.Core.Models;
using TwitchBot.Data.Repositories;
using System.Threading;

namespace TwitchBot.Core.Services
{
    public class ProxyService : IProxyService
    {
        private readonly ILogger<ProxyService> _logger;
        private readonly IProxyRepository _proxyRepository;
        private readonly SemaphoreSlim _validationLock = new(1, 1);
        private readonly Random _random = new();

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

            await _validationLock.WaitAsync();
            try
            {
                var lines = await File.ReadAllLinesAsync(filePath);
                var existingProxies = await _proxyRepository.GetAll();
                var existingDict = existingProxies.ToDictionary(p => $"{p.Address}:{p.Port}");

                int addedCount = 0;
                int updatedCount = 0;

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
                            if (existingProxy.Username != username || existingProxy.Password != password)
                            {
                                existingProxy.Username = username;
                                existingProxy.Password = password;
                                await _proxyRepository.UpdateProxy(existingProxy);
                                updatedCount++;
                            }
                        }
                        else
                        {
                            var proxy = new ProxyServer
                            {
                                Address = address,
                                Port = port,
                                Username = username,
                                Password = password,
                                Type = ProxyType.SOCKS5,
                                IsValid = false,
                                LastChecked = DateTime.MinValue
                            };

                            await _proxyRepository.AddProxy(proxy);
                            addedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing proxy line: {Line}", line);
                    }
                }

                _logger.LogInformation($"Proxies loaded: {addedCount} added, {updatedCount} updated");
                return addedCount + updatedCount;
            }
            finally
            {
                _validationLock.Release();
            }
        }

        public async Task<ProxyValidationResult> ValidateProxy(ProxyServer proxy)
        {
            var result = new ProxyValidationResult
            {
                Proxy = proxy,
                TestUrl = TwitchTestUrl,
                StartTime = DateTime.UtcNow
            };

            try
            {
                // 1. Проверка TCP подключения
                if (!await CheckTcpReachability(proxy))
                {
                    return FinalizeResult(result, false, "TCP connection failed");
                }

                // 2. Проверка HTTP через прокси
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
                            if (url == TwitchTestUrl && !content.Contains("twitch", StringComparison.OrdinalIgnoreCase))
                            {
                                return FinalizeResult(result, false, "Invalid Twitch response");
                            }

                            return FinalizeResult(result, true, null);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Proxy test failed for {Url}", url);
                    }
                }

                return FinalizeResult(result, false, "All test URLs failed");
            }
            catch (Exception ex)
            {
                return FinalizeResult(result, false, GetSimplifiedError(ex));
            }
        }

        public async Task<List<ProxyValidationResult>> ValidateAllProxies()
        {
            await _validationLock.WaitAsync();
            try
            {
                var proxies = await _proxyRepository.GetAll();
                var results = new List<ProxyValidationResult>();
                var batchSize = 10;
                var processed = 0;

                while (processed < proxies.Count)
                {
                    var batch = proxies.Skip(processed).Take(batchSize).ToList();
                    var batchTasks = batch.Select(ValidateProxy).ToList();

                    await Task.WhenAll(batchTasks);
                    results.AddRange(batchTasks.Select(t => t.Result));

                    foreach (var proxy in batch)
                    {
                        var result = batchTasks.First(t => t.Result.Proxy.Id == proxy.Id).Result;
                        proxy.IsValid = result.IsValid;
                        proxy.LastChecked = DateTime.UtcNow;
                        await _proxyRepository.UpdateProxy(proxy);
                    }

                    processed += batchSize;
                    if (processed < proxies.Count)
                    {
                        await Task.Delay(5000); // Задержка между батчами
                    }
                }

                return results;
            }
            finally
            {
                _validationLock.Release();
            }
        }

        public async Task<List<ProxyServer>> GetValidProxies()
        {
            try
            {
                return await _proxyRepository.GetValidProxies();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting valid proxies");
                return new List<ProxyServer>();
            }
        }

        public async Task<ProxyServer> GetRandomValidProxy()
        {
            var validProxies = await GetValidProxies();
            if (!validProxies.Any())
            {
                throw new InvalidOperationException("No valid proxies available");
            }

            return validProxies[_random.Next(validProxies.Count)];
        }

        public async Task AddProxy(ProxyServer proxy)
        {
            try
            {
                proxy.IsValid = false;
                proxy.LastChecked = DateTime.MinValue;
                await _proxyRepository.AddProxy(proxy);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding proxy");
                throw;
            }
        }

        public async Task UpdateProxy(ProxyServer proxy)
        {
            try
            {
                await _proxyRepository.UpdateProxy(proxy);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating proxy");
                throw;
            }
        }

        public async Task<int> GetProxyCount()
        {
            try
            {
                return await _proxyRepository.GetCount();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting proxy count");
                return 0;
            }
        }

        public async Task<bool> IsProxyValid(ProxyServer proxy)
        {
            try
            {
                using var httpClient = CreateHttpClient(proxy);
                var response = await httpClient.GetAsync("https://www.twitch.tv");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
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
                    {"User-Agent", GetRandomUserAgent()},
                    {"Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8"},
                    {"Accept-Language", "en-US,en;q=0.9"}
                }
            };
        }

        private string GetRandomUserAgent()
        {
            var agents = new[]
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:89.0) Gecko/20100101 Firefox/89.0",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36"
            };
            return agents[_random.Next(agents.Length)];
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
            string? errorMessage)
        {
            result.IsValid = isValid;
            result.ErrorMessage = errorMessage;
            result.ResponseTime = DateTime.UtcNow - result.StartTime;
            return result;
        }
    }
}