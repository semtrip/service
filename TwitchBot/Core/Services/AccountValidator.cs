using System;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using TwitchBot.Core.Models;

namespace TwitchBot.Core.Services
{
    public class AccountValidator : IAccountValidator
    {
        private readonly ILogger<AccountValidator> _logger;
        private readonly WebDriverPool _driverPool;

        public AccountValidator(
            ILogger<AccountValidator> logger,
            WebDriverPool driverPool)
        {
            _logger = logger;
            _driverPool = driverPool;
        }

        public async Task<TwitchAccount> ValidateAccount(TwitchAccount account, ProxyServer proxy)
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
                    _driverPool.ReleaseDriver(driver, proxy);
            }
        }

        private async Task<TwitchAccount> AuthenticateWithCookies(IWebDriver driver, TwitchAccount account)
        {
            var cookiesDict = new Dictionary<string, string>();
            try
            {
                driver.Navigate().GoToUrl("https://www.twitch.tv");
                var cookie = new OpenQA.Selenium.Cookie("auth-token", account.AuthToken, ".twitch.tv", "/", DateTime.Now.AddYears(1));
                driver.Manage().Cookies.AddCookie(cookie);
                driver.Navigate().Refresh();

                await Task.Delay(5000);

                var isValid = driver.FindElements(By.CssSelector("[data-a-target='user-menu-toggle']")).Count > 0;

                if (isValid) {
                    var seleniumCookies = driver.Manage().Cookies.AllCookies;
                    cookiesDict = seleniumCookies.ToDictionary(
                        c => c.Name,
                        c => c.Value
                    );
                    account.Cookies = JsonSerializer.Serialize(cookiesDict);
                    // Добавляем время экспирации (24 часа)
                    cookiesDict["expires"] = DateTime.UtcNow.AddDays(1).ToString("o");
                }
                driver.Close();
                account.IsValid = isValid;
                return account;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Auth error for {account.Username}");
                return account;
            }
        }
    }
}