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
        private IWebDriver _driver;

        public TwitchService(ILogger<TwitchService> logger, WebDriverPool driverPool)
        {
            _logger = logger;
            _driverPool = driverPool;
            InitializeBrowser();
        }

        public void InitializeBrowser()
        {
            if (_driver != null)
            {
                _logger.LogWarning("Браузер уже инициализирован.");
                return;
            }

            var options = new ChromeOptions();

            // Настройки для обхода детекции
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
            options.AddArgument("--headless=new");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--disable-dev-shm-usage");

            _driver = new ChromeDriver(options);

            // Удаление webdriver-флага
            ((IJavaScriptExecutor)_driver).ExecuteScript("Object.defineProperty(navigator, 'webdriver', { get: () => undefined })");

            _logger.LogInformation("Браузер успешно инициализирован");
        }

        public async Task<bool> IsStreamLive(string channelUrl)
        {
            
            try
            {
                if (_driver == null)
                {
                    _logger.LogError("Браузер не инициализирован");
                    return false;
                }

                _logger.LogInformation($"Проверка стрима на канале {channelUrl}");

                var originalWindow = _driver.CurrentWindowHandle;

                ((IJavaScriptExecutor)_driver).ExecuteScript("window.open();");
                _driver.SwitchTo().Window(_driver.WindowHandles.Last());
                _driver.Navigate().GoToUrl(channelUrl);

                await Task.Delay(5000);

                // Проверяем индикатор живого стрима
                var liveIndicator = _driver.FindElements(By.CssSelector("[data-a-target='live-indicator']"));
                bool isLive = liveIndicator.Any();

                // Закрываем вкладку и возвращаемся
                _driver.Close();
                _driver.SwitchTo().Window(originalWindow);

                return isLive;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при проверке стрима: {channelUrl}");
                return false;
            }
        }

        public async Task<bool> VerifyAccount(TwitchAccount account, ProxyServer proxy)
        {
            using var proxyDriverPool = new WebDriverPool(2, proxy);
            IWebDriver driver = null;
            try
            {
                driver = await proxyDriverPool.GetDriver();
                return await AuthenticateWithCookies(driver, account);
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

                if (!await AuthenticateWithCookies(driver, account))
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
                    _logger.LogInformation($"Авторизация через {account.Username} Выполнена успешно");
                    return true;
                }
                else
                {
                    _logger.LogInformation($"Авторизация через {account.Username} Не удачна. Уккаунт не валиден!");
                    return false;
                }
            }
            catch (Exception ex) {
                _logger.LogInformation($"Ошибка при авторизации аккаунта {account.Username} MESSAGE: {ex.Message}");
                return false;
            }            
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