using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Core.Services
{
    public interface ITwitchService
    {
        Task<bool> IsStreamLive(string channelUrl);
        Task<bool> IsStreamLiveApi(string ChanelName);
        Task<bool> VerifyAccount(TwitchAccount account, ProxyServer proxy);
        Task WatchStream(TwitchAccount account, ProxyServer proxy, string channelUrl, int minutes);
        Task WatchAsGuest(ProxyServer proxy, string channelUrl, int minutes);
    }
}