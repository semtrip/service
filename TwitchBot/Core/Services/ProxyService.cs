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

namespace TwitchViewerBot.Core.Services
{
    public class ProxyService : IProxyService
    {
        private readonly ILogger<ProxyService> _logger;
        private readonly IProxyRepository _proxyRepository;
        private const int ConnectionTimeout = 5000;
        private const string TwitchTestUrl = "https://www.twitch.tv";
        private const string TestUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36";

        public ProxyService(
            ILogger<ProxyService> logger,
            IProxyRepository proxyRepository)
        {
            _logger = logger;
            _proxyRepository = proxyRepository;
        }

        public async Task<List<ProxyValidationResult>> ValidateAllProxies()
        {
            var results = new List<ProxyValidationResult>();
            var proxies = await _proxyRepository.GetAll();

            foreach (var proxy in proxies)
            {
                var result = await ValidateProxyWithRetry(proxy, maxRetries: 2);
                results.Add(result);
                
                proxy.IsValid = result.IsValid;
                proxy.LastChecked = DateTime.UtcNow;
                await _proxyRepository.UpdateProxy(proxy);
            }

            return results;
        }

        public async Task<ProxyValidationResult> ValidateProxy(ProxyServer proxy)
        {
            var result = new ProxyValidationResult { Proxy = proxy };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (!await CheckTcpConnection(proxy.Address, proxy.Port))
                {
                    return FailResult(result, "TCP connection failed", stopwatch);
                }

                var (httpSuccess, httpError) = await CheckHttpAccess(proxy);
                if (!httpSuccess)
                {
                    return FailResult(result, $"HTTP failed: {httpError}", stopwatch);
                }

                var (twitchSuccess, twitchError) = await CheckTwitchAccess(proxy);
                if (!twitchSuccess)
                {
                    return FailResult(result, $"Twitch failed: {twitchError}", stopwatch);
                }

                if (!string.IsNullOrEmpty(proxy.Username))
                {
                    var (authSuccess, authError) = await CheckProxyAuthorization(proxy);
                    if (!authSuccess)
                    {
                        return FailResult(result, $"Auth failed: {authError}", stopwatch);
                    }
                }

                return SuccessResult(result, "Proxy is valid", stopwatch);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Proxy validation error for {proxy.Address}:{proxy.Port}");
                return FailResult(result, $"Validation error: {ex.Message}", stopwatch);
            }
        }

        public async Task<List<ProxyServer>> GetValidProxies()
        {
            return await _proxyRepository.GetValidProxies();
        }

        public async Task<bool> TestProxyConnection(ProxyServer proxy)
        {
            return await CheckTcpConnection(proxy.Address, proxy.Port);
        }

        public async Task<int> LoadProxiesFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogError("Proxy file not found: {FilePath}", filePath);
                return 0;
            }

            try
            {
                var proxyLines = await File.ReadAllLinesAsync(filePath);
                var proxies = new List<ProxyServer>();

                foreach (var line in proxyLines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Trim().Split(new[] { ':', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && IPAddress.TryParse(parts[0], out _) && int.TryParse(parts[1], out var port))
                    {
                        var proxy = new ProxyServer
                        {
                            Address = parts[0],
                            Port = port,
                            Username = parts.Length > 2 ? parts[2] : string.Empty,
                            Password = parts.Length > 3 ? parts[3] : string.Empty,
                            IsValid = false,
                            LastChecked = DateTime.MinValue
                        };
                        proxies.Add(proxy);
                    }
                }

                if (proxies.Any())
                {
                    return await _proxyRepository.BulkInsertProxies(proxies);
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading proxies from file");
                return 0;
            }
        }

        public async Task<int> GetProxyCount()
        {
            return await _proxyRepository.GetCount();
        }

        private async Task<ProxyValidationResult> ValidateProxyWithRetry(ProxyServer proxy, int maxRetries)
        {
            ProxyValidationResult result = null!;
            for (int i = 0; i < maxRetries; i++)
            {
                result = await ValidateProxy(proxy);
                if (result.IsValid) break;
                await Task.Delay(1000);
            }
            return result;
        }

        private async Task<bool> CheckTcpConnection(string address, int port)
        {
            using var tcpClient = new TcpClient();
            var connectTask = tcpClient.ConnectAsync(address, port);
            var timeoutTask = Task.Delay(ConnectionTimeout);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                return false;
            }

            return tcpClient.Connected;
        }

        private async Task<(bool Success, string Error)> CheckHttpAccess(ProxyServer proxy)
        {
            try
            {
                using var handler = CreateHttpClientHandler(proxy);
                using var client = CreateHttpClient(handler);

                var response = await client.GetAsync("http://httpbin.org/ip");
                return (response.IsSuccessStatusCode, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private async Task<(bool Success, string Error)> CheckTwitchAccess(ProxyServer proxy)
        {
            try
            {
                using var handler = CreateHttpClientHandler(proxy);
                using var client = CreateHttpClient(handler);

                var response = await client.GetAsync(TwitchTestUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return (false, $"HTTP {response.StatusCode}");
                }

                var content = await response.Content.ReadAsStringAsync();
                if (!content.Contains("twitch.tv"))
                {
                    return (false, "Twitch content not found");
                }

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private async Task<(bool Success, string Error)> CheckProxyAuthorization(ProxyServer proxy)
        {
            try
            {
                var invalidHandler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"{proxy.Address}:{proxy.Port}")
                    {
                        Credentials = new NetworkCredential("invalid", "invalid")
                    },
                    UseProxy = true,
                    ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true
                };

                using var invalidClient = CreateHttpClient(invalidHandler);
                try
                {
                    await invalidClient.GetAsync("http://httpbin.org/ip");
                    return (false, "Proxy accepted invalid credentials");
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("407"))
                {
                    var validHandler = CreateHttpClientHandler(proxy);
                    using var validClient = CreateHttpClient(validHandler);

                    var response = await validClient.GetAsync("http://httpbin.org/ip");
                    return (response.IsSuccessStatusCode, string.Empty);
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private HttpClientHandler CreateHttpClientHandler(ProxyServer proxy)
        {
            var handler = new HttpClientHandler
            {
                Proxy = new WebProxy($"{proxy.Address}:{proxy.Port}"),
                UseProxy = true,
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true
            };

            if (!string.IsNullOrEmpty(proxy.Username))
            {
                handler.Proxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);
            }

            return handler;
        }

        private HttpClient CreateHttpClient(HttpClientHandler handler)
        {
            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15),
                DefaultRequestHeaders =
                {
                    {"User-Agent", TestUserAgent}
                }
            };
        }

        private ProxyValidationResult SuccessResult(ProxyValidationResult result, string message, Stopwatch stopwatch)
        {
            stopwatch.Stop();
            result.IsValid = true;
            result.ErrorMessage = message;
            result.ResponseTime = stopwatch.Elapsed;
            _logger.LogInformation($"Proxy {result.Proxy.Address}:{result.Proxy.Port} - VALID - {result.ResponseTime.TotalMilliseconds}ms");
            return result;
        }

        private ProxyValidationResult FailResult(ProxyValidationResult result, string error, Stopwatch stopwatch)
        {
            stopwatch.Stop();
            result.IsValid = false;
            result.ErrorMessage = error;
            result.ResponseTime = stopwatch.Elapsed;
            _logger.LogWarning($"Proxy {result.Proxy.Address}:{result.Proxy.Port} - INVALID - {error}");
            return result;
        }
    }
}