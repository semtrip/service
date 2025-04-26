using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Core.Services
{
    public interface ITwitchService
    {
        void InitializeBrowser();
        Task<bool> IsStreamLive(string channelUrl);
        Task<bool> VerifyAccount(TwitchAccount account, ProxyServer proxy);
        Task WatchStream(TwitchAccount account, ProxyServer proxy, string channelUrl, int minutes);
        Task WatchAsGuest(ProxyServer proxy, string channelUrl, int minutes);
    }
}