using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Linq;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Core.Services
{
    public class TwitchService : ITwitchService
    {
        private readonly ILogger<TwitchService> _logger;

        public TwitchService(ILogger<TwitchService> logger)
        {
            _logger = logger;
        }

        public async Task<bool> VerifyAccount(TwitchAccount account, ProxyServer proxy)
        {
            var options = new ChromeOptions();
            ConfigureBrowserOptions(options, proxy);
            options.AddArgument("--headless");

            try
            {
                using var driver = new ChromeDriver(options);
                driver.Navigate().GoToUrl("https://www.twitch.tv/login");
                ((IJavaScriptExecutor)driver).ExecuteScript(
                    $"localStorage.setItem('auth_token', '{account.AuthToken}');");
                await Task.Delay(5000);
                driver.Navigate().GoToUrl("https://www.twitch.tv");
                var isLoggedIn = driver.FindElements(By.CssSelector("[data-a-target='user-menu-toggle']")).Any();
                driver.Quit();
                return isLoggedIn;
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
                if (account != null)
                {
                    driver.Navigate().GoToUrl("https://www.twitch.tv/login");
                    ((IJavaScriptExecutor)driver).ExecuteScript(
                        $"localStorage.setItem('auth_token', '{account.AuthToken}');");
                    await Task.Delay(5000);
                }

                driver.Navigate().GoToUrl(channelUrl);
                await Task.Delay(60000);

                var endTime = DateTime.Now.AddMinutes(minutes);
                var random = new Random();
                while (DateTime.Now < endTime)
                {
                    await Task.Delay(random.Next(30000, 120000));
                    if (random.NextDouble() > 0.8)
                    {
                        driver.Navigate().Refresh();
                        await Task.Delay(15000);
                    }
                }
            }
            finally
            {
                driver.Quit();
            }
        }

        private void ConfigureBrowserOptions(ChromeOptions options, ProxyServer proxy)
        {
            if (proxy != null)
            {
                options.AddArgument($"--proxy-server=http://{proxy.Address}");
                options.AddArgument($"--proxy-auth={proxy.Username}:{proxy.Password}");
            }
            options.AddArgument("--disable-blink-features=AutomationControlled");
        }
    }
}