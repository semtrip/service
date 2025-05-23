﻿using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using TwitchBot.Core.Models;
using TwitchBot.Core.Services;
using TwitchBot.Core.Models;
using OpenQA.Selenium.DevTools;


namespace TwitchBot.Core.Services
{
    public class TwitchService : ITwitchService, IDisposable
    {
        private readonly ILogger<TwitchService> _logger;
        private readonly WebDriverPool _driverPool;
        private readonly IAccountService _accountService;
        private IWebDriver _mainDriver;
        private readonly Random _random = new();
        private readonly List<string> _userAgents = new()
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:89.0) Gecko/20100101 Firefox/89.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36"
        };
        public TwitchService(
            ILogger<TwitchService> logger,
            WebDriverPool driverPool,
            IAccountService accountService)
        {
            _logger = logger;
            _driverPool = driverPool;
            _accountService = accountService;
            InitializeMainDriver();
        }

        private void InitializeMainDriver()
        {
            var options = new ChromeOptions
            {
                PageLoadStrategy = PageLoadStrategy.Normal
            };

            options.AddArguments(
                //"--headless=new",
                "--disable-gpu",
                "--no-sandbox",
                "--disable-dev-shm-usage",
                "--window-size=1280,720",
                "--mute-audio",
                "--disable-extensions",
                "--disable-notifications",
                "--disable-blink-features=AutomationControlled");

            var service = ChromeDriverService.CreateDefaultService();
            service.SuppressInitialDiagnosticInformation = true;
            service.HideCommandPromptWindow = true;

            _mainDriver = new ChromeDriver(service, options);
            _logger.LogInformation("Main driver initialized");
        }

        public async Task<bool> IsStreamLive(string channelUrl)
        {
            try
            {
                var originalWindow = _mainDriver.CurrentWindowHandle;
                ((IJavaScriptExecutor)_mainDriver).ExecuteScript("window.open();");
                _mainDriver.SwitchTo().Window(_mainDriver.WindowHandles.Last());
                _mainDriver.Navigate().GoToUrl(channelUrl);

                await Task.Delay(5000);

                var liveIndicator = _mainDriver.FindElements(By.CssSelector("span.CoreText-sc-1txzju1-0.bfNjIO"));
                bool isLive = liveIndicator.Any(e => e.Displayed);

                _mainDriver.Close();
                _mainDriver.SwitchTo().Window(originalWindow);

                return isLive;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking stream: {channelUrl}");
                return false;
            }
        }

        public async Task WatchStream(
            IWebDriver driver,
            TwitchAccount account,
            ProxyServer proxy,
            string channelUrl,
            int minutes)
        {
            try
            {
                if (!await AuthenticateWithCookies(driver, account))
                {
                    _logger.LogError($"Auth failed for {account.Username}");
                    return;
                }

                await NavigateAndWatch(driver, channelUrl, minutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error watching stream with {account.Username}");
                throw;
            }
        }

        public async Task WatchAsGuest(
            IWebDriver driver,
            ProxyServer proxy,
            string channelUrl,
            int minutes)
        {
            try
            {
                await NavigateAndWatch(driver, channelUrl, minutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error watching as guest");
                throw;
            }
        }

        private async Task<bool> AuthenticateWithCookies(IWebDriver driver, TwitchAccount account)
        {
            try
            {
                driver.Navigate().GoToUrl("https://www.twitch.tv");
                var cookie = new OpenQA.Selenium.Cookie("auth-token", account.AuthToken, ".twitch.tv", "/", DateTime.Now.AddYears(1));
                driver.Manage().Cookies.AddCookie(cookie);
                driver.Navigate().Refresh();
                await Task.Delay(5000);

                var isAuth = driver.FindElements(By.CssSelector("[data-a-target='user-menu-toggle']")).Count > 0;

                if (isAuth)
                {
                    _logger.LogInformation($"Auth successful for {account.Username}");
                    return true;
                }

                _logger.LogWarning($"Auth failed for {account.Username}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Auth error for {account.Username}");
                return false;
            }
        }

        private async Task NavigateAndWatch(IWebDriver driver, string channelUrl, int minutes)
        {
            driver.Navigate().GoToUrl(channelUrl);
            await Task.Delay(10000);

            var endTime = DateTime.Now.AddMinutes(minutes);
            while (DateTime.Now < endTime)
            {
                try
                {
                    driver.FindElement(By.TagName("body")).SendKeys(Keys.Space);
                    await Task.Delay(30000);
                }
                catch
                {
                    await Task.Delay(5000);
                }
            }
        }
        public async Task WatchLightweight(TwitchAccount account, ProxyServer proxy, string channelUrl, int minutes)
        {
            using var client = CreateHttpClient(proxy);
            if (account != null)
            {
                _logger.LogInformation($"[{account.Username}] Начинаю просмотр стрима {channelUrl} через прокси {proxy.Address}:{proxy.Port}");

                try
                {
                    var cookies = await GetOrRefreshAccountCookies(account, proxy);
                    var cookiesHeader = FormatCookiesHeader(cookies);

                    if (!string.IsNullOrEmpty(cookiesHeader))
                    {
                        client.DefaultRequestHeaders.Add("Cookie", cookiesHeader);
                        _logger.LogInformation($"[{account.Username}] Использую куки: {cookiesHeader}");
                    }
                    else
                    {
                        _logger.LogWarning($"[{account.Username}] Не удалось получить куки");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[{account.Username}] Ошибка при работе с куками");
                    return;
                }
            }
            else
            {
                _logger.LogInformation($"[Гость] Начинаю просмотр стрима {channelUrl} через прокси {proxy.Address}:{proxy.Port}");
            }

            // Настройка заголовков
            client.DefaultRequestHeaders.Add("User-Agent", _userAgents[_random.Next(_userAgents.Count)]);
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
            client.DefaultRequestHeaders.Add("Referer", "https://www.twitch.tv/");
            client.DefaultRequestHeaders.Add("Origin", "https://www.twitch.tv");

            var endTime = DateTime.UtcNow.AddMinutes(minutes);
            var channelName = GetChannelName(channelUrl);

            try
            {
                // 1. Первоначальный запрос
                _logger.LogInformation($"[{(account?.Username ?? "Гость")}] Отправляю первоначальный запрос...");
                var response = await client.GetAsync(channelUrl);
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"[{(account?.Username ?? "Гость")}] Получен ответ: {response.StatusCode}");

                // 2. Извлекаем clientId
                var clientId = ExtractClientId(content);
                var deviceId = Guid.NewGuid().ToString();
                _logger.LogInformation($"[{(account?.Username ?? "Гость")}] ClientID: {clientId}, DeviceID: {deviceId}");

                // 3. Инициализация просмотра
                _logger.LogInformation($"[{(account?.Username ?? "Гость")}] Инициализирую просмотр...");
                var initResponse = await InitializeViewer(client, channelName, clientId, deviceId);
                _logger.LogInformation($"[{(account?.Username ?? "Гость")}] Инициализация завершена: {initResponse.StatusCode}");

                int heartbeatCount = 0;
                while (DateTime.UtcNow < endTime)
                {
                    // 4. Heartbeat-запрос
                    heartbeatCount++;
                    _logger.LogInformation($"[{(account?.Username ?? "Гость")}] Отправляю heartbeat #{heartbeatCount}...");
                    var heartbeatResponse = await SendHeartbeat(client, channelName, clientId, deviceId);
                    _logger.LogInformation($"[{(account?.Username ?? "Гость")}] Heartbeat #{heartbeatCount} ответ: {heartbeatResponse.StatusCode}");

                    // 5. Случайные действия
                    if (_random.Next(0, 100) < 30)
                    {
                        _logger.LogInformation($"[{(account?.Username ?? "Гость")}] Имитирую активность...");
                        await EmulateUserActivity(client, channelUrl);
                    }

                    var delay = _random.Next(20000, 40000);
                    _logger.LogInformation($"[{(account?.Username ?? "Гость")}] Следующий запрос через {delay/1000} сек...");
                    await Task.Delay(delay);
                }

                _logger.LogInformation($"[{(account?.Username ?? "Гость")}] Просмотр завершен успешно");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[{(account?.Username ?? "Гость")}] Ошибка при просмотре {channelUrl}");
            }
        }

        private async Task<Dictionary<string, string>> GetOrRefreshAccountCookies(TwitchAccount account, ProxyServer proxy)
        {
            // Инициализация пустого словаря, если куки отсутствуют
            var cookiesDict = new Dictionary<string, string>();

            // Проверяем наличие и валидность кук в БД
            if (!string.IsNullOrEmpty(account.Cookies) && account.Cookies != "[]")
            {
                try
                {
                    cookiesDict = JsonSerializer.Deserialize<Dictionary<string, string>>(account.Cookies)
                                  ?? new Dictionary<string, string>();

                    if (cookiesDict.TryGetValue("expires", out var expiresStr) &&
                        DateTime.TryParse(expiresStr, out var expires) &&
                        expires > DateTime.UtcNow.AddHours(1))
                    {
                        _logger.LogInformation($"[{account.Username}] Использую сохраненные куки");
                        return cookiesDict;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, $"[{account.Username}] Ошибка десериализации кук, получаем новые");
                }
            }

            // Получаем новые куки через Selenium
            _logger.LogInformation($"[{account.Username}] Получаю новые куки...");
            using var driver = await _driverPool.GetDriver(proxy);
            try
            {
                driver.Navigate().GoToUrl("https://www.twitch.tv");
                var cookie = new OpenQA.Selenium.Cookie("auth-token", account.AuthToken, ".twitch.tv", "/", DateTime.Now.AddYears(1));
                driver.Manage().Cookies.AddCookie(cookie);
                driver.Navigate().Refresh();
                await Task.Delay(5000);

                // Получаем все куки из браузера
                var seleniumCookies = driver.Manage().Cookies.AllCookies;
                cookiesDict = seleniumCookies.ToDictionary(
                    c => c.Name,
                    c => c.Value
                );

                // Добавляем время экспирации (24 часа)
                cookiesDict["expires"] = DateTime.UtcNow.AddDays(1).ToString("o");

                // Сохраняем в БД
                account.Cookies = JsonSerializer.Serialize(cookiesDict);
                await _accountService.UpdateAccount(account);
                _logger.LogInformation($"[{account.Username}] Получено {cookiesDict.Count} кук, сохранено в БД");

                return cookiesDict;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[{account.Username}] Ошибка при получении кук");
                return cookiesDict;
            }
            finally
            {
                _driverPool.ReleaseDriver(driver, proxy);
            }
        }

        private string FormatCookiesHeader(Dictionary<string, string> cookies)
        {
            if (cookies == null || cookies.Count == 0)
                return string.Empty;

            return string.Join("; ", cookies
                .Where(kv => kv.Key != "expires" && !string.IsNullOrEmpty(kv.Value))
                .Select(kv => $"{kv.Key}={kv.Value}"));
        }

        private async Task<HttpResponseMessage> InitializeViewer(HttpClient client, string channelName, string clientId, string deviceId)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql")
            {
                Headers =
        {
            { "Client-ID", clientId },
            { "X-Device-Id", deviceId }
        },
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        operationName = "ViewerCard_ViewerState",
                        variables = new { channelLogin = channelName },
                        extensions = new
                        {
                            persistedQuery = new
                            {
                                version = 1,
                                sha256Hash = "b5f5e2e1e5a5e3e5e2e1e5a5e3e5e2e1e5a5e3e5e2e1e5a5e3e5e2e1e5a5e3e5"
                            }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json")
            };

            return await client.SendAsync(request);
        }

        private async Task<HttpResponseMessage> SendHeartbeat(HttpClient client, string channelName, string clientId, string deviceId)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql")
            {
                Headers =
        {
            { "Client-ID", clientId },
            { "X-Device-Id", deviceId }
        },
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        operationName = "ViewerHeartbeat",
                        variables = new { channelLogin = channelName },
                        extensions = new
                        {
                            persistedQuery = new
                            {
                                version = 1,
                                sha256Hash = "5a5e3e5e2e1e5a5e3e5e2e1e5a5e3e5e2e1e5a5e3e5e2e1e5a5e3e5e2e1e5a5"
                            }
                        }
                    }),
                    Encoding.UTF8,
                    "application/json")
            };

            return await client.SendAsync(request);
        }

        private async Task EmulateUserActivity(HttpClient client, string channelUrl)
        {
            try
            {
                // 1. Запрос чата
                var chatResponse = await client.GetAsync($"{channelUrl}/chat");
                _logger.LogInformation($"Chat response: {chatResponse.StatusCode}");

                // 2. Запрос информации о стриме
                var streamInfoResponse = await client.GetAsync($"{channelUrl}/about");
                _logger.LogInformation($"Stream info response: {streamInfoResponse.StatusCode}");

                // 3. Случайное действие
                var actions = new[]
                {
            async () => await client.GetAsync($"{channelUrl}/schedule"),
            async () => await client.GetAsync($"{channelUrl}/videos")
        };
                await actions[_random.Next(actions.Length)]();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user activity emulation");
            }
        }

        private string ExtractClientId(string htmlContent)
        {
            // Регулярное выражение для извлечения clientId из HTML
            var match = Regex.Match(htmlContent, @"clientId=""([a-z0-9]{32})""");
            return match.Success ? match.Groups[1].Value : "kimne78kx3ncx6brgo4mv6wki5h1ko"; // fallback clientId
        }

        private HttpClient CreateHttpClient(ProxyServer proxy)
        {
            var handler = new HttpClientHandler
            {
                Proxy = proxy.Type == ProxyType.SOCKS5
                    ? new WebProxy($"socks5://{proxy.Address}:{proxy.Port}") 
                    {
                        Credentials = new NetworkCredential(proxy.Username, proxy.Password)
                    }
                    : new WebProxy($"http://{proxy.Address}:{proxy.Port}"),
                UseProxy = true
            };

            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        private string GetChannelName(string channelUrl)
        {
            return channelUrl.Split('/').LastOrDefault();
        }

        public void Dispose()
        {
            try
            {
                _mainDriver?.Quit();
                _mainDriver?.Dispose();
            }
            catch { }
        }
    }
}