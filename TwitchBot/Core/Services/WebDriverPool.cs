using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Bogus;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using TwitchBot.Core.Models;

namespace TwitchBot.Core.Services
{
    public class WebDriverPool : IDisposable
    {
        private readonly ConcurrentBag<IWebDriver> _drivers = new();
        private readonly SemaphoreSlim _semaphore;
        private readonly int _maxDrivers;
        private readonly Timer _cleanupTimer;
        private bool _disposed;
        private readonly Faker _faker = new();
        private readonly ILogger<WebDriverPool> _logger;
        private readonly ProxyServer _proxy;
        private readonly ChromeOptions _baseOptions;

        public WebDriverPool(
            int maxDrivers,
            ProxyServer proxy = null,
            ILogger<WebDriverPool> logger = null)
        {
            _maxDrivers = maxDrivers;
            _logger = logger;
            _proxy = proxy;
            _semaphore = new SemaphoreSlim(maxDrivers, maxDrivers);
            _cleanupTimer = new Timer(_ => CleanupIdleDrivers(), null,
                TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));

            _baseOptions = CreateBaseChromeOptions();
        }

        public async Task<IWebDriver> GetDriver()
        {
            await _semaphore.WaitAsync();
            try
            {
                return _drivers.TryTake(out var driver)
                    ? driver
                    : CreateNewDriver();
            }
            catch (Exception ex)
            {
                _semaphore.Release();
                _logger?.LogError(ex, "Error creating new WebDriver");
                throw;
            }
        }

        public void ReleaseDriver(IWebDriver driver)
        {
            if (driver == null) return;

            try
            {
                driver.Manage().Cookies.DeleteAllCookies();
                ((IJavaScriptExecutor)driver).ExecuteScript("window.localStorage.clear();");
                ((IJavaScriptExecutor)driver).ExecuteScript("window.sessionStorage.clear();");
                _drivers.Add(driver);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error cleaning driver state");
                driver?.Quit();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public IWebDriver CreateNewDriver()
        {
            // Копируем базовые настройки
            var options = new ChromeOptions();
            foreach (var argument in _baseOptions.Arguments)
            {
                options.AddArgument(argument);
            }

            // Добавляем случайные настройки
            ConfigureRandomSettings(options);

            var service = ChromeDriverService.CreateDefaultService();
            ConfigureDriverService(service);

            var driver = new ChromeDriver(service, options, TimeSpan.FromSeconds(60));
            ConfigureDriver(driver);

            return driver;
        }

        private void ConfigureRandomSettings(ChromeOptions options)
        {
            var (width, height) = GetRandomResolution();
            options.AddArgument($"--window-size={width},{height}");
            options.AddArgument($"--user-agent={GetRandomUserAgent()}");
        }

        private void ConfigureDriverService(ChromeDriverService service)
        {
            service.SuppressInitialDiagnosticInformation = true;
            service.HideCommandPromptWindow = true;
            service.EnableVerboseLogging = false;
        }

        private void ConfigureDriver(IWebDriver driver)
        {
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
            driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(5);

            RemoveAutomationFlags(driver);
            SetRandomGeolocation(driver);
            SetRandomScreenProps(driver);
        }

        private void RemoveAutomationFlags(IWebDriver driver)
        {
            var scripts = new[]
            {
                "Object.defineProperty(navigator, 'webdriver', { get: () => undefined })",
                "Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3] })",
                "Object.defineProperty(navigator, 'languages', { get: () => ['en-US', 'en'] })"
            };

            foreach (var script in scripts)
            {
                try
                {
                    ((IJavaScriptExecutor)driver).ExecuteScript(script);
                }
                catch { /* Ignore */ }
            }
        }

        private (int width, int height) GetRandomResolution()
        {
            var resolutions = new[]
            {
                (1920, 1080), (1366, 768), (1536, 864),
                (1440, 900), (1280, 720), (1600, 900)
            };
            return _faker.PickRandom(resolutions);
        }

        private string GetRandomUserAgent()
        {
            var agents = new[]
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:89.0) Gecko/20100101 Firefox/89.0",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36"
            };
            return _faker.PickRandom(agents);
        }

        private void SetRandomGeolocation(IWebDriver driver)
        {
            try
            {
                var lat = _faker.Random.Double(-90, 90);
                var lng = _faker.Random.Double(-180, 180);

                var script = $@"
                    navigator.geolocation.getCurrentPosition = function(success) {{
                        success({{
                            coords: {{
                                latitude: {lat},
                                longitude: {lng},
                                accuracy: {_faker.Random.Double(1, 100)}
                            }},
                            timestamp: Date.now()
                        }});
                    }};
                    navigator.geolocation.watchPosition = navigator.geolocation.getCurrentPosition;";

                ((IJavaScriptExecutor)driver).ExecuteScript(script);
            }
            catch { /* Ignore */ }
        }

        private void SetRandomScreenProps(IWebDriver driver)
        {
            try
            {
                var script = $@"
                    Object.defineProperty(window.screen, 'width', {{ get: () => {_faker.Random.Number(1000, 2000)} }});
                    Object.defineProperty(window.screen, 'height', {{ get: () => {_faker.Random.Number(700, 1400)} }});
                    Object.defineProperty(window.screen, 'availWidth', {{ get: () => {_faker.Random.Number(900, 1900)} }});
                    Object.defineProperty(window.screen, 'availHeight', {{ get: () => {_faker.Random.Number(600, 1300)} }});
                    Object.defineProperty(window.screen, 'colorDepth', {{ get: () => 24 }});
                    Object.defineProperty(window.screen, 'pixelDepth', {{ get: () => 24 }});";

                ((IJavaScriptExecutor)driver).ExecuteScript(script);
            }
            catch { /* Ignore */ }
        }

        private ChromeOptions CreateBaseChromeOptions()
        {
            var options = new ChromeOptions();

            options.AddArguments(
                "--headless=new",
                "--disable-gpu",
                "--no-sandbox",
                "--disable-dev-shm-usage",
                "--mute-audio",
                "--disable-extensions",
                "--disable-notifications",
                "--disable-blink-features=AutomationControlled",
                "--disable-infobars",
                "--single-process",
                "--no-zygote",
                "--disable-popup-blocking");

            // Современный способ добавления исключенных аргументов
            options.AddExcludedArgument("enable-automation");

            // Добавляем proxy если есть
            if (_proxy != null)
            {
                var proxy = new Proxy
                {
                    Kind = ProxyKind.Manual,
                    IsAutoDetect = false,
                    HttpProxy = $"{_proxy.Address}:{_proxy.Port}",
                    SslProxy = $"{_proxy.Address}:{_proxy.Port}"
                };

                if (!string.IsNullOrEmpty(_proxy.Username))
                {
                    proxy.SocksUserName = _proxy.Username;
                    proxy.SocksPassword = _proxy.Password;
                }

                options.Proxy = proxy;
            }

            return options;
        }

        private void CleanupIdleDrivers()
        {
            while (_drivers.Count > _maxDrivers / 2)
            {
                if (_drivers.TryTake(out var driver))
                {
                    try { driver.Quit(); } catch { /* Ignore */ }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cleanupTimer?.Dispose();
            _semaphore?.Dispose();

            foreach (var driver in _drivers)
            {
                try { driver.Quit(); } catch { /* Ignore */ }
            }
            _drivers.Clear();
        }
    }
}