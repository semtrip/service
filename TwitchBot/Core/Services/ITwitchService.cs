using System.Threading.Tasks;
using TwitchBot.Core.Models;

namespace TwitchBot.Core.Services
{
    public interface ITwitchService
    {
        Task<bool> IsStreamLive(string channelUrl);
        Task<bool> VerifyAccount(TwitchAccount account, ProxyServer proxy);
        Task WatchStream(TwitchAccount account, ProxyServer proxy, string channelUrl, int minutes);
        Task WatchAsGuest(ProxyServer proxy, string channelUrl, int minutes);
    }
}