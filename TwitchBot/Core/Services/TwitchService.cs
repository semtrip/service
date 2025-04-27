using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using TwitchBot.Core.Models;
using TwitchBot.Core.Services;
using TwitchBot.Core.Models;

namespace TwitchBot.Core.Services
{
    public class TwitchService : ITwitchService, IDisposable
    {
        private readonly ILogger<TwitchService> _logger;
        private readonly WebDriverPool _driverPool;
        private readonly IProxyService _proxyService;
        private IWebDriver _mainDriver;

        public TwitchService(
            ILogger<TwitchService> logger,
            WebDriverPool driverPool,
            IProxyService proxyService)
        {
            _logger = logger;
            _driverPool = driverPool;
            _proxyService = proxyService;
            InitializeMainDriver();
        }

        private void InitializeMainDriver()
        {
            var options = new ChromeOptions
            {
                PageLoadStrategy = PageLoadStrategy.Normal
            };

            options.AddArguments(
                "--headless=new",
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

                var liveIndicator = _mainDriver.FindElements(By.CssSelector("[data-a-target='live-indicator']"));
                bool isLive = liveIndicator.Any();

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
                driver = await _driverPool.GetDriver(proxy);
                return await AuthenticateWithCookies(driver, account);
            }
            finally
            {
                if (driver != null)
                {
                    _driverPool.ReleaseDriver(driver, proxy);
                }
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