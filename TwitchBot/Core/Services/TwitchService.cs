using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Bogus;
using TwitchBot.Core.Models;

namespace TwitchBot.Core.Services
{
    public class TwitchService : ITwitchService, IDisposable
    {
        private readonly ILogger<TwitchService> _logger;
        private readonly WebDriverPool _driverPool;
        private readonly Random _random = new();
        private readonly Faker _faker = new();
        private IWebDriver _mainDriver;
        private bool _disposed;

        public TwitchService(
            ILogger<TwitchService> logger,
            WebDriverPool driverPool)
        {
            _logger = logger;
            _driverPool = driverPool;
            InitializeMainDriver();
        }

        private void InitializeMainDriver()
        {
            try
            {
                // Используем пул драйверов вместо прямого создания
                _mainDriver = _driverPool.CreateNewDriver();
                _logger.LogInformation("Main driver initialized");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize main driver");
                throw;
            }
        }

        public async Task<bool> IsStreamLive(string channelUrl)
        {
            if (_mainDriver == null) return false;

            try
            {
                var originalWindow = _mainDriver.CurrentWindowHandle;
                ((IJavaScriptExecutor)_mainDriver).ExecuteScript("window.open();");
                _mainDriver.SwitchTo().Window(_mainDriver.WindowHandles.Last());

                _mainDriver.Navigate().GoToUrl(channelUrl);
                await Task.Delay(5000 + _random.Next(1000, 3000));

                var liveIndicator = _mainDriver.FindElements(
                    By.CssSelector("[data-a-target='live-indicator'], .live-indicator"));

                bool isLive = liveIndicator.Any(e => e.Displayed && e.Enabled);

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

        public async Task<bool> VerifyAccount(TwitchAccount account, ProxyServer proxy)
        {
            IWebDriver driver = null;
            try
            {
                driver = await _driverPool.GetDriver();
                return await AuthenticateWithCookies(driver, account);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Account verification failed: {account.Username}");
                return false;
            }
            finally
            {
                if (driver != null)
                    _driverPool.ReleaseDriver(driver);
            }
        }

        public async Task WatchStream(
            TwitchAccount account,
            ProxyServer proxy,
            string channelUrl,
            int minutes)
        {
            IWebDriver driver = null;
            try
            {
                driver = await _driverPool.GetDriver();

                if (!await AuthenticateWithCookies(driver, account))
                {
                    _logger.LogError($"Auth failed for {account.Username}");
                    return;
                }

                _logger.LogInformation($"Watching stream with {account.Username}");
                await NavigateAndWatch(driver, channelUrl, minutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error watching stream with {account.Username}");
            }
            finally
            {
                if (driver != null)
                    _driverPool.ReleaseDriver(driver);
            }
        }

        public async Task WatchAsGuest(ProxyServer proxy, string channelUrl, int minutes)
        {
            IWebDriver driver = null;
            try
            {
                driver = await _driverPool.GetDriver();
                await NavigateAndWatch(driver, channelUrl, minutes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in guest watching");
            }
            finally
            {
                if (driver != null)
                    _driverPool.ReleaseDriver(driver);
            }
        }

        private async Task<bool> AuthenticateWithCookies(IWebDriver driver, TwitchAccount account)
        {
            try
            {
                driver.Navigate().GoToUrl("https://www.twitch.tv");
                var cookie = new Cookie(
                    "auth-token",
                    account.AuthToken,
                    ".twitch.tv",
                    "/",
                    DateTime.Now.AddYears(1));

                driver.Manage().Cookies.AddCookie(cookie);
                driver.Navigate().Refresh();
                await Task.Delay(3000 + _random.Next(1000, 3000));

                var userMenu = driver.FindElements(
                    By.CssSelector("[data-a-target='user-menu-toggle']"));

                return userMenu.Count > 0 && userMenu[0].Displayed;
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
            await Task.Delay(15000 + _random.Next(5000, 10000));

            var endTime = DateTime.Now.AddMinutes(minutes);
            while (DateTime.Now < endTime)
            {
                try
                {
                    await PerformRandomAction(driver);
                    await Task.Delay(_random.Next(30000, 90000));
                }
                catch { /* Ignore */ }
            }
        }

        private async Task PerformRandomAction(IWebDriver driver)
        {
            var action = _random.Next(0, 5);
            switch (action)
            {
                case 0:
                    ScrollRandomly(driver);
                    break;
                case 1:
                    //ClickRandomElement(driver);
                    break;
                case 2:
                    if (_random.Next(0, 10) == 0)
                        driver.Navigate().Refresh();
                    break;
                case 3:
                    MoveMouseRandomly(driver);
                    break;
                case 4:
                    PauseVideo(driver);
                    break;
            }
        }

        private void ScrollRandomly(IWebDriver driver)
        {
            try
            {
                var scrollAmount = _random.Next(200, 800);
                var script = $"window.scrollBy(0, {scrollAmount});";
                ((IJavaScriptExecutor)driver).ExecuteScript(script);
            }
            catch { /* Ignore */ }
        }

        private void ClickRandomElement(IWebDriver driver)
        {
            try
            {
                var elements = driver.FindElements(
                    By.CssSelector("button, a, [role='button']"))
                    .Where(e => e.Displayed && e.Enabled)
                    .ToList();

                if (elements.Count > 0)
                {
                    var element = elements[_random.Next(0, elements.Count)];
                    element.Click();
                }
            }
            catch { /* Ignore */ }
        }

        private void MoveMouseRandomly(IWebDriver driver)
        {
            try
            {
                var x = _random.Next(0, 1000);
                var y = _random.Next(0, 700);
                var script = $@"
                    var evt = new MouseEvent('mousemove', {{
                        clientX: {x},
                        clientY: {y},
                        bubbles: true,
                        cancelable: true,
                        view: window
                    }});
                    document.dispatchEvent(evt);";

                ((IJavaScriptExecutor)driver).ExecuteScript(script);
            }
            catch { /* Ignore */ }
        }

        private void PauseVideo(IWebDriver driver)
        {
            try
            {
                var script = @"
                    var video = document.querySelector('video');
                    if (video) {
                        video.paused ? video.play() : video.pause();
                    }";
                ((IJavaScriptExecutor)driver).ExecuteScript(script);
            }
            catch { /* Ignore */ }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _mainDriver?.Quit();
                _driverPool?.Dispose();
            }
            catch { /* Ignore */ }
        }
    }
}