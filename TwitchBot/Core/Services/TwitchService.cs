// TwitchBot/Core/Services/TwitchService.cs
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Core.Services
{
    public class TwitchService : ITwitchService, IDisposable
    {
        private readonly ILogger<TwitchService> _logger;
        private readonly WebDriverPool _driverPool;
        private readonly Random _random = new();

        public TwitchService(ILogger<TwitchService> logger, WebDriverPool driverPool)
        {
            _logger = logger;
            _driverPool = driverPool;
        }

        public async Task<bool> IsStreamLive(string channelUrl)
        {
            IWebDriver driver = null;
            try
            {
                driver = await _driverPool.GetDriver();
                driver.Navigate().GoToUrl(channelUrl);
                await Task.Delay(5000); // Ждем загрузки

                try
                {
                    var liveIndicator = driver.FindElement(By.CssSelector("[data-a-target='stream-status-indicator']"));
                    return liveIndicator.Text.Contains("LIVE") || liveIndicator.Text.Contains("В ЭФИРЕ");
                }
                catch { return false; }
            }
            finally
            {
                if (driver != null)
                    _driverPool.ReleaseDriver(driver);
            }
        }

        public async Task<bool> VerifyAccount(TwitchAccount account, ProxyServer proxy)
        {
            using var proxyDriverPool = new WebDriverPool(2, proxy);
            IWebDriver driver = null;
            try
            {
                driver = await proxyDriverPool.GetDriver();
                return await AuthenticateWithCookies(driver, account.AuthToken);
            }
            finally
            {
                if (driver != null)
                    proxyDriverPool.ReleaseDriver(driver);
            }
        }

        public async Task WatchStream(TwitchAccount account, ProxyServer proxy, string channelUrl, int minutes)
        {
            using var proxyDriverPool = new WebDriverPool(2, proxy);
            IWebDriver driver = null;
            try
            {
                driver = await proxyDriverPool.GetDriver();

                if (!await AuthenticateWithCookies(driver, account.AuthToken))
                {
                    _logger.LogError($"Auth failed for {account.Username}");
                    return;
                }
                _logger.LogInformation($"Просматриваем стрим с {account.Username}");

                await NavigateAndWatch(driver, channelUrl, minutes);
            }
            finally
            {
                if (driver != null)
                    proxyDriverPool.ReleaseDriver(driver);
            }
        }

        public async Task WatchAsGuest(ProxyServer proxy, string channelUrl, int minutes)
        {
            using var proxyDriverPool = new WebDriverPool(2, proxy);
            IWebDriver driver = null;
            try
            {
                driver = await proxyDriverPool.GetDriver();
                await NavigateAndWatch(driver, channelUrl, minutes);
            }
            finally
            {
                if (driver != null)
                    proxyDriverPool.ReleaseDriver(driver);
            }
        }

        private async Task<bool> AuthenticateWithCookies(IWebDriver driver, string authToken)
        {
            driver.Navigate().GoToUrl("https://www.twitch.tv");
            var cookie = new OpenQA.Selenium.Cookie("auth-token", authToken, ".twitch.tv", "/", DateTime.Now.AddYears(1));
            driver.Manage().Cookies.AddCookie(cookie);
            driver.Navigate().Refresh();
            await Task.Delay(5000);

            try
            {
                return driver.FindElements(By.CssSelector("[data-a-target='user-menu-toggle']")).Count > 0;
            }
            catch { return false; }
        }

        private async Task NavigateAndWatch(IWebDriver driver, string channelUrl, int minutes)
        {
            driver.Navigate().GoToUrl(channelUrl);
            await Task.Delay(30000); // Минимальное время просмотра

            var endTime = DateTime.Now.AddMinutes(minutes);
            while (DateTime.Now < endTime)
            {
                try
                {
                    switch (_random.Next(0, 4))
                    {
                        case 0:
                            ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollBy(0, 200)");
                            break;
                        case 1:
                            var elements = driver.FindElements(By.CssSelector("button, a"));
                            if (elements.Count > 0) elements[_random.Next(0, elements.Count)].Click();
                            break;
                        case 2:
                            if (_random.Next(0, 10) == 0) driver.Navigate().Refresh();
                            break;
                    }
                    await Task.Delay(_random.Next(30000, 90000));
                }
                catch { /* Игнорируем ошибки */ }
            }
        }

        public void Dispose()
        {
            _driverPool?.Dispose();
        }
    }
}