// TwitchBot/Core/Services/WebDriverPool.cs
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Chrome.ChromeDriverExtensions;
using TwitchViewerBot.Core.Models;
using WebDriverManager;
using WebDriverManager.DriverConfigs.Impl;
using Bogus;
using Microsoft.Extensions.Logging;
using Serilog.Core;

namespace TwitchViewerBot.Core.Services
{
    public class WebDriverPool : IDisposable
    {
        private readonly ConcurrentBag<IWebDriver> _drivers = new();
        private readonly SemaphoreSlim _semaphore;
        private readonly ChromeOptions _options;
        private readonly int _maxDrivers;
        private readonly Timer _cleanupTimer;
        private bool _disposed;

        public WebDriverPool(int maxDrivers, ProxyServer proxy = null)
        {
            _maxDrivers = maxDrivers;
            _semaphore = new SemaphoreSlim(maxDrivers, maxDrivers);
            _options = CreateChromeOptions(proxy);
            _cleanupTimer = new Timer(_ => CleanupIdleDrivers(), null,
                TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));

            new DriverManager().SetUpDriver(new ChromeConfig());
        }

        public async Task<IWebDriver> GetDriver()
        {
            await _semaphore.WaitAsync();
            return _drivers.TryTake(out var driver) ? driver : CreateNewDriver();
        }

        public void ReleaseDriver(IWebDriver driver)
        {
            if (driver == null) return;

            try
            {
                // Очистка перед возвращением в пул
                driver.Manage().Cookies.DeleteAllCookies();
                ((IJavaScriptExecutor)driver).ExecuteScript("window.localStorage.clear();");
                ((IJavaScriptExecutor)driver).ExecuteScript("window.sessionStorage.clear();");
            }
            catch { /* Ignore */ }

            _drivers.Add(driver);
            _semaphore.Release();
        }

        private IWebDriver CreateNewDriver()
        {
            var service = ChromeDriverService.CreateDefaultService();
            service.SuppressInitialDiagnosticInformation = true;
            service.HideCommandPromptWindow = true;

            var driver = new ChromeDriver(service, _options, TimeSpan.FromSeconds(60));
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
            return driver;
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

        private static ChromeOptions CreateChromeOptions(ProxyServer proxy)
        {
            var options = new ChromeOptions();
            options.AddArguments(
                "--disable-gpu",
                "--no-sandbox",
                "--disable-dev-shm-usage",
                "--window-size=1280,720",
                "--mute-audio",
                "--disable-extensions",
                "--disable-notifications",
                "--disable-blink-features=AutomationControlled",
                "--disable-infobars",
                "--single-process",
                "--no-zygote",
                "--disable-popup-blocking",
                "--enable-unsafe-swiftshader");

            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            
            var faker = new Faker();
            var randomUserAgent = faker.Internet.UserAgent();
            options.AddArgument($"--user-agent={randomUserAgent}");
            Console.WriteLine($"Используемый User-Agent: {randomUserAgent}");
            if (proxy != null)
            {
                options.AddHttpProxy(proxy.Address, proxy.Port, proxy.Username ?? "", proxy.Password ?? "");
            }

            return options;
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