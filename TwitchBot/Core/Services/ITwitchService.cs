using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Core.Services
{
    public interface ITwitchService
    {
        Task<bool> VerifyAccount(TwitchAccount account, ProxyServer proxy);
        Task WatchStream(TwitchAccount account, ProxyServer proxy, string channelUrl, int minutes);
    }
}