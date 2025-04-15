using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
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
        private const int DefaultTimeoutSeconds = 10;
        private const string TwitchTestUrl = "https://www.twitch.tv";
        private const string TestUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36";

        public ProxyService(
            ILogger<ProxyService> logger,
            IProxyRepository proxyRepository)
        {
            _logger = logger;
            _proxyRepository = proxyRepository;
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
                    else
                    {
                        _logger.LogWarning("Invalid proxy format: {Line}", line);
                    }
                }

                if (proxies.Any())
                {
                    var count = await _proxyRepository.BulkInsertProxies(proxies);
                    _logger.LogInformation("Successfully loaded {Count} proxies from file", count);
                    return count;
                }

                _logger.LogWarning("No valid proxies found in file");
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading proxies from file");
                return 0;
            }
        }

        public async Task<bool> TestProxy(ProxyServer proxy)
        {
            var result = await ValidateProxy(proxy);
            return result.IsValid;
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
                // 1. Check proxy reachability
                var (reachabilitySuccess, reachabilityError) = await CheckProxyReachability(proxy);
                if (!reachabilitySuccess)
                {
                    return FinalizeResult(result, false, $"Proxy unreachable: {reachabilityError}", stopwatch);
                }

                // 2. Check HTTPS connectivity
                var (httpsSuccess, httpsError) = await CheckHttpsConnectivity(proxy);
                if (!httpsSuccess)
                {
                    return FinalizeResult(result, false, $"HTTPS failed: {httpsError}", stopwatch);
                }

                // 3. Check Twitch access
                var (twitchSuccess, twitchError) = await CheckTwitchAccess(proxy);
                if (!twitchSuccess)
                {
                    return FinalizeResult(result, false, $"Twitch blocked: {twitchError}", stopwatch);
                }

                // 4. Check proxy auth if credentials exist
                if (!string.IsNullOrEmpty(proxy.Username))
                {
                    var (authSuccess, authError) = await CheckProxyAuthorization(proxy);
                    if (!authSuccess)
                    {
                        return FinalizeResult(result, false, $"Auth failed: {authError}", stopwatch);
                    }
                }

                return FinalizeResult(result, true, "Proxy works with Twitch", stopwatch);
            }
            catch (Exception ex)
            {
                return FinalizeResult(result, false, $"Validation error: {ex.Message}", stopwatch);
            }
        }

        public async Task<List<ProxyValidationResult>> ValidateAllProxies()
        {
            var proxies = await _proxyRepository.GetAll();
            
            if (!proxies.Any())
            {
                _logger.LogWarning("No proxies found in database. Please load proxies first.");
                return new List<ProxyValidationResult>();
            }

            _logger.LogInformation("Starting validation of {Count} proxies...", proxies.Count);
            
            var results = new List<ProxyValidationResult>();
            foreach (var proxy in proxies)
            {
                var result = await ValidateProxy(proxy);
                results.Add(result);
            }

            var validCount = results.Count(r => r.IsValid);
            _logger.LogInformation("Proxy validation completed. Valid: {Valid}, Invalid: {Invalid}", 
                validCount, proxies.Count - validCount);
            
            return results;
        }

        public async Task<List<ProxyServer>> GetValidProxies()
        {
            return await _proxyRepository.GetValidProxies();
        }

        public async Task<int> GetProxyCount()
        {
            return await _proxyRepository.GetCount();
        }

        private async Task<(bool Success, string Error)> CheckProxyReachability(ProxyServer proxy)
        {
            try
            {
                using var tcpClient = new TcpClient();
                var connectTask = tcpClient.ConnectAsync(proxy.Address, proxy.Port);

                if (await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(5))) != connectTask)
                {
                    return (false, "Connection timeout (5s)");
                }

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private async Task<(bool Success, string Error)> CheckHttpsConnectivity(ProxyServer proxy)
        {
            try
            {
                using var handler = CreateHttpClientHandler(proxy);
                using var client = CreateHttpClient(handler, proxy, TimeSpan.FromSeconds(DefaultTimeoutSeconds));

                var response = await client.GetAsync("https://httpbin.org/get");
                return response.IsSuccessStatusCode 
                    ? (true, string.Empty) 
                    : (false, $"HTTP {(int)response.StatusCode} {response.StatusCode}");
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
                using var client = CreateHttpClient(handler, proxy, TimeSpan.FromSeconds(15));

                var response = await client.GetAsync(TwitchTestUrl);
                
                if (!response.IsSuccessStatusCode)
                {
                    return (false, $"HTTP {(int)response.StatusCode}");
                }

                var content = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(content) || content.Length < 1000)
                {
                    return (false, "Invalid content length");
                }

                if (!content.Contains("twitch.tv") || !content.Contains("Twitch"))
                {
                    return (false, "Twitch content missing");
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
                var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"{proxy.Address}:{proxy.Port}")
                    {
                        Credentials = new NetworkCredential("invalid_user", "invalid_pass")
                    },
                    UseProxy = true
                };

                using var client = CreateHttpClient(handler, proxy, TimeSpan.FromSeconds(5));
                
                try
                {
                    await client.GetAsync("http://httpbin.org/ip");
                    return (false, "Accepted invalid credentials");
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("407"))
                {
                    return (true, string.Empty);
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
                ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            if (!string.IsNullOrEmpty(proxy.Username))
            {
                handler.Proxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);
            }

            return handler;
        }

        private HttpClient CreateHttpClient(HttpClientHandler handler, ProxyServer proxy, TimeSpan timeout)
        {
            var client = new HttpClient(handler)
            {
                Timeout = timeout,
                DefaultRequestHeaders =
                {
                    {"User-Agent", TestUserAgent},
                    {"Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8"},
                    {"Accept-Language", "en-US,en;q=0.5"},
                    {"Accept-Encoding", "gzip, deflate, br"}
                }
            };

            return client;
        }

        private ProxyValidationResult FinalizeResult(
            ProxyValidationResult result,
            bool isValid,
            string message,
            Stopwatch stopwatch)
        {
            stopwatch.Stop();
            
            result.IsValid = isValid;
            result.ErrorMessage = isValid ? string.Empty : message;
            result.ResponseTime = stopwatch.Elapsed;
            
            LogResult(result);
            UpdateProxyInDatabase(result);
            
            return result;
        }

        private void LogResult(ProxyValidationResult result)
        {
            var originalColor = Console.ForegroundColor;
            
            Console.Write($"[{DateTime.UtcNow:HH:mm:ss}] ");
            Console.Write($"Proxy {result.Proxy.Address}:{result.Proxy.Port} - ");
            
            Console.ForegroundColor = result.IsValid ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write(result.IsValid ? "VALID" : "INVALID");
            
            Console.ForegroundColor = originalColor;
            Console.WriteLine($" - {result.ResponseTime.TotalMilliseconds}ms - {result.ErrorMessage ?? "Success"}");

            var logMessage = $"Proxy {result.Proxy.Address}:{result.Proxy.Port} - " +
                           $"{(result.IsValid ? "VALID" : "INVALID")} - " +
                           $"{result.ResponseTime.TotalMilliseconds}ms - " +
                           $"{result.ErrorMessage ?? "Success"}";

            if (result.IsValid)
            {
                _logger.LogInformation(logMessage);
            }
            else
            {
                _logger.LogWarning(logMessage);
            }
        }

        private void UpdateProxyInDatabase(ProxyValidationResult result)
        {
            try
            {
                result.Proxy.IsValid = result.IsValid;
                result.Proxy.LastChecked = DateTime.UtcNow;
                _proxyRepository.UpdateProxy(result.Proxy);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update proxy in database");
            }
        }
    }
}