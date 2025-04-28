using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Chrome.ChromeDriverExtensions;
using TwitchBot.Core.Models;
using TwitchBot.Core.Models;

namespace TwitchBot.Core.Services
{
    public sealed class WebDriverPool : IDisposable
    {
        private readonly ConcurrentDictionary<ProxyServer, ConcurrentBag<IWebDriver>> _proxyDrivers = new();
        private readonly SemaphoreSlim _semaphore;
        private readonly ChromeOptions _baseOptions;
        private bool _disposed;

        public WebDriverPool(int maxConcurrentDrivers)
        {
            _semaphore = new SemaphoreSlim(maxConcurrentDrivers, maxConcurrentDrivers);
            _baseOptions = new ChromeOptions
            {
                PageLoadStrategy = PageLoadStrategy.Normal
            };

            _baseOptions.AddArguments(
                "--headless=new",
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
                "--disable-popup-blocking");
        }

        public async Task<IWebDriver> GetDriver(ProxyServer proxy = null, CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebDriverPool));

            await _semaphore.WaitAsync(ct);

            try
            {
                if (proxy != null && _proxyDrivers.TryGetValue(proxy, out var drivers) && drivers.TryTake(out var driver))
                {
                    return driver;
                }

                return CreateNewDriver(proxy);
            }
            catch
            {
                _semaphore.Release();
                throw;
            }
        }

        public void ReleaseDriver(IWebDriver driver, ProxyServer proxy = null)
        {
            if (_disposed || driver == null)
            {
                driver?.Dispose();
                return;
            }

            try
            {
                driver.Manage().Cookies.DeleteAllCookies();

                if (proxy != null)
                {
                    var drivers = _proxyDrivers.GetOrAdd(proxy, _ => new ConcurrentBag<IWebDriver>());
                    drivers.Add(driver);
                }
                else
                {
                    driver.Dispose();
                }
            }
            catch
            {
                driver.Dispose();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private ChromeDriver CreateNewDriver(ProxyServer proxy)
        {
            var options = new ChromeOptions();
            options.AddArguments(_baseOptions.Arguments);

            if (proxy != null)
            {
                options.AddHttpProxy(proxy.Address, proxy.Port, proxy.Username ?? "", proxy.Password ?? "");
            }

            var service = ChromeDriverService.CreateDefaultService();
            service.SuppressInitialDiagnosticInformation = true;
            service.HideCommandPromptWindow = true;

            var driver = new ChromeDriver(service, options, TimeSpan.FromSeconds(30));
            driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);
            return driver;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var drivers in _proxyDrivers.Values)
            {
                while (drivers.TryTake(out var driver))
                {
                    try
                    {
                        driver.Quit();
                        driver.Dispose();
                    }
                    catch { }
                }
            }

            _proxyDrivers.Clear();
            _semaphore.Dispose();
        }
    }
}