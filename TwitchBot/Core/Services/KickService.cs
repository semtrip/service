using System;
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
    public class KickService : ITwitchService, IDisposable
    {
        private readonly ILogger<TwitchService> _logger;
        private readonly WebDriverPool _driverPool;
        private IWebDriver _mainDriver;
        private readonly Random _random = new();
        private readonly List<string> _userAgents = new()
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:89.0) Gecko/20100101 Firefox/89.0",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36"
        };
        public KickService(
            ILogger<TwitchService> logger,
            WebDriverPool driverPool)
        {
            _logger = logger;
            _driverPool = driverPool;
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

                await Task.Delay(10000);

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
        public async Task WatchLightweight(
        TwitchAccount account,
        ProxyServer proxy,
        string channelUrl,
        int minutes)
        {
            using var client = CreateHttpClient(proxy);
            if (account != null)
            {
                _logger.LogInformation($"Начинаю просмотр стрима {channelUrl} с аккаунта {account.Username} с прокси {proxy.Address}");
                client.DefaultRequestHeaders.Add("Cookie", $"auth-token={account.AuthToken}; persistent=1");
            }
            else
            {
                _logger.LogInformation($"Начинаю просмотр стрима {channelUrl} гостем с прокси {proxy.Address}");
            }

            client.DefaultRequestHeaders.Add("User-Agent", _userAgents[_random.Next(_userAgents.Count)]);
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
            client.DefaultRequestHeaders.Add("Referer", "https://www.twitch.tv/");
            client.DefaultRequestHeaders.Add("Origin", "https://www.twitch.tv");

            var endTime = DateTime.UtcNow.AddMinutes(minutes);
            var channelName = GetChannelName(channelUrl);

            try
            {
                // 1. Первоначальный запрос для установки соединения
                var response = await client.GetAsync(channelUrl);
                var content = await response.Content.ReadAsStringAsync();

                // 2. Извлечение необходимых токенов из HTML
                var clientId = ExtractClientId(content);
                var deviceId = Guid.NewGuid().ToString();

                // 3. Инициализация просмотра
                await InitializeViewer(client, channelName, clientId, deviceId);

                while (DateTime.UtcNow < endTime)
                {
                    // 4. Периодические heartbeat-запросы
                    await SendHeartbeat(client, channelName, clientId, deviceId);

                    // 5. Случайные действия для имитации активности
                    if (_random.Next(0, 100) < 30)
                    {
                        //await EmulateUserActivity(client, channelName);
                    }

                    await Task.Delay(_random.Next(20000, 40000));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при просмотре {channelUrl}");
            }
        }

        private async Task InitializeViewer(HttpClient client, string channelName, string clientId, string deviceId)
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

            await client.SendAsync(request);
        }

        private async Task SendHeartbeat(HttpClient client, string channelName, string clientId, string deviceId)
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

            await client.SendAsync(request);
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