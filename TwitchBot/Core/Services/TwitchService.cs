using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using TwitchViewerBot.Core.Models;
using SeleniumCookie = OpenQA.Selenium.Cookie;

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
            var options = new ChromeOptions();
            options.AddArguments(
                "--headless=new", // Новый headless режим
                "--disable-gpu",
                "--no-sandbox",
                "--disable-dev-shm-usage", // Важно для Docker/лимитов памяти
                "--window-size=1920,1080",
                "--mute-audio",
                "--disable-extensions",
                "--disable-notifications",
                "--log-level=3"
            );

            try
            {
                using var driver = new ChromeDriver(ChromeDriverService.CreateDefaultService(), options, TimeSpan.FromSeconds(30));
                driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(20);

                driver.Navigate().GoToUrl(channelUrl);

                // Ждем появления индикатора live
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
                try
                {
                    var liveIndicator = wait.Until(d =>
                        d.FindElements(By.CssSelector("[data-a-target='live-indicator']"))
                        .FirstOrDefault(e => e.Displayed));

                    return liveIndicator != null;
                }
                catch (WebDriverTimeoutException)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking stream: {Url}", channelUrl);
                throw new Exception("Browser initialization error. Check ChromeDriver installation.");
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