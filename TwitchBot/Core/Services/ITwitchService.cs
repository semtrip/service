using System.Threading.Tasks;
using TwitchBot.Core.Models;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace TwitchBot.Core.Services
{
    public interface ITwitchService
    {
        Task<bool> IsStreamLive(string channelUrl);
        Task<bool> VerifyAccount(TwitchAccount account, ProxyServer proxy);
        Task WatchStream(IWebDriver driver, TwitchAccount account, ProxyServer proxy, string channelUrl, int minutes);
        Task WatchAsGuest(IWebDriver driver, ProxyServer proxy, string channelUrl, int minutes);
    }
}