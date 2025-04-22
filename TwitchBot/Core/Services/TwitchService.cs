using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using TwitchViewerBot.Core.Models;
using SeleniumCookie = OpenQA.Selenium.Cookie;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;
using System.Diagnostics;

namespace TwitchViewerBot.Core.Services
{
    public class TwitchService : ITwitchService
    {
        private readonly ILogger<TwitchService> _logger;
        private readonly Random _random;
        private const string AuthCookieName = "auth-token";

        public TwitchService(ILogger<TwitchService> logger)
        {
            _logger = logger;
            _random = new Random();
        }

        public async Task<bool> IsStreamLive(string channelUrl)
        {
            // Проверяем валидность URL
            if (!Uri.TryCreate(channelUrl, UriKind.Absolute, out var uri) ||
                uri.Host != "www.twitch.tv")
            {
                _logger.LogError($"Invalid Twitch URL: {channelUrl}");
                return false;
            }

            try
            {
                new DriverManager().SetUpDriver(new ChromeConfig());

                var options = new ChromeOptions();
                options.AddArguments(
                    "--headless=new",
                    "--disable-gpu",
                    "--no-sandbox",
                    "--disable-dev-shm-usage",
                    "--window-size=1920,1080",
                    "--mute-audio",
                    "--disable-extensions",
                    "--disable-notifications",
                    "--log-level=3",
                    "--enable-unsafe-swiftshader",
                    $"user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{GetChromeMajorVersion()} Safari/537.36"
                );

                options.AddExcludedArgument("enable-automation");
                options.AddAdditionalOption("useAutomationExtension", false);

                using var driver = new ChromeDriver(ChromeDriverService.CreateDefaultService(), options, TimeSpan.FromSeconds(30));

                // Удаляем признаки автоматизации
                ((IJavaScriptExecutor)driver).ExecuteScript(
                    "Object.defineProperty(navigator, 'webdriver', {get: () => undefined})");

                driver.Navigate().GoToUrl(channelUrl);
                await Task.Delay(10000); // Увеличиваем время ожидания

                try
                {
                    // Новые селекторы для 2024 года
                    var liveElements = driver.FindElements(By.XPath(
                        "//*[contains(@data-a-target,'live-indicator') or contains(text(),'LIVE') or contains(@aria-label,'live')]"));

                    return liveElements.Any(e => e.Displayed);
                }
                catch
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Browser initialization failed");
                return false;
            }
        }

        private string GetChromeMajorVersion()
        {
            try
            {
                var chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
                var versionInfo = FileVersionInfo.GetVersionInfo(chromePath);
                return versionInfo.FileMajorPart.ToString();
            }
            catch
            {
                return "119"; // Версия по умолчанию
            }
        }
        public async Task<bool> IsStreamLiveApi(string channelName)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Client-ID", "kimne78kx3ncx6brgo4mv6wki5h1ko"); // Стандартный Client-ID Twitch

                var response = await httpClient.GetAsync(
                    $"https://gql.twitch.tv/gql?query=%7B%0A%20%20user%28login%3A%22{channelName}%22%29%20%7B%0A%20%20%20%20stream%20%7B%0A%20%20%20%20%20%20id%0A%20%20%20%20%7D%0A%20%20%7D%0A%7D");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return content.Contains("\"stream\":{\"id\"") || content.Contains("\"type\":\"live\"");
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        public async Task<bool> VerifyAccount(TwitchAccount account, ProxyServer proxy)
        {
            ChromeDriver driver = null;
            try
            {
                var options = ConfigureBrowserOptions(new ChromeOptions(), proxy);
                options.AddArgument("--headless");

                driver = new ChromeDriver(ChromeDriverService.CreateDefaultService(), options, TimeSpan.FromSeconds(60));
                return await AuthenticateWithCookies(driver, account.AuthToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Account verification failed: {account.Username}");
                return false;
            }
            finally
            {
                driver?.Quit();
                driver?.Dispose();
            }
        }

        public async Task WatchStream(TwitchAccount account, ProxyServer proxy, string channelUrl, int minutes)
        {
            ChromeDriver driver = null;
            try
            {
                var options = ConfigureBrowserOptions(new ChromeOptions(), proxy);
                options.AddArgument("--mute-audio");

                driver = new ChromeDriver(ChromeDriverService.CreateDefaultService(), options, TimeSpan.FromSeconds(60));

                if (!await AuthenticateWithCookies(driver, account.AuthToken))
                {
                    _logger.LogWarning($"Auth failed for account: {account.Username}");
                    return;
                }

                await WatchChannel(driver, channelUrl, minutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error watching stream for account {account.Username}");
                throw;
            }
            finally
            {
                driver?.Quit();
                driver?.Dispose();
            }
        }

        public async Task WatchAsGuest(ProxyServer proxy, string channelUrl, int minutes)
        {
            ChromeDriver driver = null;
            try
            {
                var options = ConfigureBrowserOptions(new ChromeOptions(), proxy);
                options.AddArgument("--mute-audio");

                driver = new ChromeDriver(ChromeDriverService.CreateDefaultService(), options, TimeSpan.FromSeconds(60));
                await WatchChannel(driver, channelUrl, minutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error watching as guest: {channelUrl}");
                throw;
            }
            finally
            {
                driver?.Quit();
                driver?.Dispose();
            }
        }

        private async Task<bool> AuthenticateWithCookies(ChromeDriver driver, string authToken)
        {
            try
            {
                driver.Navigate().GoToUrl("https://www.twitch.tv");

                var authCookie = new SeleniumCookie(
                    name: AuthCookieName,
                    value: authToken,
                    domain: ".twitch.tv",
                    path: "/",
                    expiry: DateTime.Now.AddYears(1));

                driver.Manage().Cookies.AddCookie(authCookie);
                driver.Navigate().Refresh();
                await Task.Delay(5000);

                return IsUserAuthenticated(driver);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cookie authentication error");
                return false;
            }
        }

        private bool IsUserAuthenticated(ChromeDriver driver)
        {
            try
            {
                return driver.FindElements(By.CssSelector("[data-a-target='user-menu-toggle']")).Any();
            }
            catch
            {
                return false;
            }
        }

        private async Task WatchChannel(ChromeDriver driver, string channelUrl, int minutes)
        {
            driver.Navigate().GoToUrl(channelUrl);
            await HumanLikeActivity(driver, minutes);
        }

        private async Task HumanLikeActivity(ChromeDriver driver, int minutes)
        {
            var endTime = DateTime.Now.AddMinutes(minutes);
            var actions = new Action[]
            {
                () => ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollBy(0, 200)"),
                () => driver.Navigate().Refresh(),
                () => ClickRandomElement(driver, ".tw-button"),
                () => SendChatMessage(driver)
            };

            while (DateTime.Now < endTime)
            {
                if (_random.NextDouble() > 0.7)
                {
                    try
                    {
                        actions[_random.Next(actions.Length)]();
                        await Task.Delay(_random.Next(2000, 5000));
                    }
                    catch { }
                }
                await Task.Delay(_random.Next(15000, 30000));
            }
        }

        private ChromeOptions ConfigureBrowserOptions(ChromeOptions options, ProxyServer proxy)
        {
            if (proxy != null)
            {
                options.AddArgument($"--proxy-server={proxy.Address}:{proxy.Port}");
                if (!string.IsNullOrEmpty(proxy.Username))
                {
                    options.AddArgument($"--proxy-auth={proxy.Username}:{proxy.Password}");
                }
            }

            options.AddArguments(
                "--disable-blink-features=AutomationControlled",
                "--disable-infobars",
                "--disable-notifications",
                "--disable-popup-blocking",
                "--disable-extensions",
                "--disable-gpu",
                "--no-sandbox",
                "--disable-dev-shm-usage");

            return options;
        }

        private void ClickRandomElement(ChromeDriver driver, string selector)
        {
            var elements = driver.FindElements(By.CssSelector(selector));
            if (elements.Count > 0)
            {
                elements[_random.Next(0, elements.Count)].Click();
            }
        }

        private void SendChatMessage(ChromeDriver driver)
        {
            try
            {
                var chatInput = driver.FindElement(By.CssSelector(".chat-input"));
                chatInput.SendKeys("Nice stream! " + _random.Next(1000));
                chatInput.SendKeys(Keys.Enter);
            }
            catch { }
        }
    }
}