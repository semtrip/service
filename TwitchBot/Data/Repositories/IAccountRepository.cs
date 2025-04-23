using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Data.Repositories
{
    public interface IAccountRepository
    {
        Task<List<TwitchAccount>> GetValidAccounts(int count);
        Task UpdateAccount(TwitchAccount account);
        Task<List<TwitchAccount>> GetAll();
        Task AddAccount(TwitchAccount account);
    }
}