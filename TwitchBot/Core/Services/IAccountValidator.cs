using TwitchBot.Core.Models;

namespace TwitchBot.Core.Services
{
    public interface IAccountValidator
    {
        Task<TwitchAccount> ValidateAccount(TwitchAccount account, ProxyServer proxy);
    }
}