using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using TwitchViewerBot.Core.Models;
using SeleniumCookie = OpenQA.Selenium.Cookie; // Алиас для разрешения конфликта имен

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
            options.AddArguments("--headless", "--mute-audio");
            
            try
            {
                using var driver = new ChromeDriver(options);
                driver.Navigate().GoToUrl(channelUrl);
                await Task.Delay(5000); // Ждем загрузки страницы

                return driver.FindElements(By.CssSelector("[data-a-target='live-indicator']")).Any();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking stream live status: {channelUrl}");
                return false;
            }
        }

        public async Task<bool> VerifyAccount(TwitchAccount account, ProxyServer proxy)
        {
            var options = new ChromeOptions();
            ConfigureBrowserOptions(options, proxy);
            options.AddArgument("--headless");

            try
            {
                using var driver = new ChromeDriver(options);
                return await AuthenticateWithCookies(driver, account.AuthToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Account verification failed: {account.Username}");
                return false;
            }
        }

        public async Task WatchStream(TwitchAccount account, ProxyServer proxy, string channelUrl, int minutes)
        {
            var options = new ChromeOptions();
            ConfigureBrowserOptions(options, proxy);
            options.AddArgument("--mute-audio");

            using var driver = new ChromeDriver(options);
            try
            {
                if (!await AuthenticateWithCookies(driver, account.AuthToken))
                {
                    _logger.LogWarning($"Auth failed for account: {account.Username}");
                    return;
                }

                await WatchChannel(driver, channelUrl, minutes);
            }
            finally
            {
                driver.Quit();
            }
        }

        public async Task WatchAsGuest(ProxyServer proxy, string channelUrl, int minutes)
        {
            var options = new ChromeOptions();
            ConfigureBrowserOptions(options, proxy);
            options.AddArgument("--mute-audio");

            using var driver = new ChromeDriver(options);
            try
            {
                await WatchChannel(driver, channelUrl, minutes);
            }
            finally
            {
                driver.Quit();
            }
        }

        private async Task<bool> AuthenticateWithCookies(IWebDriver driver, string authToken)
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

        private bool IsUserAuthenticated(IWebDriver driver)
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

        private async Task WatchChannel(IWebDriver driver, string channelUrl, int minutes)
        {
            driver.Navigate().GoToUrl(channelUrl);
            await HumanLikeActivity(driver, minutes);
        }

        private async Task HumanLikeActivity(IWebDriver driver, int minutes)
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

        private void ClickRandomElement(IWebDriver driver, string selector)
        {
            var elements = driver.FindElements(By.CssSelector(selector));
            if (elements.Count > 0)
            {
                elements[_random.Next(0, elements.Count)].Click();
            }
        }

        private void SendChatMessage(IWebDriver driver)
        {
            try
            {
                var chatInput = driver.FindElement(By.CssSelector(".chat-input"));
                chatInput.SendKeys("Nice stream! " + _random.Next(1000));
                chatInput.SendKeys(Keys.Enter);
            }
            catch { }
        }

        private void ConfigureBrowserOptions(ChromeOptions options, ProxyServer proxy)
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
                "--no-sandbox");
        }
    }
}