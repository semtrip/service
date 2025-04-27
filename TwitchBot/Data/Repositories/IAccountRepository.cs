using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchBot.Core.Models;

namespace TwitchBot.Data.Repositories
{
    public interface IAccountRepository
    {
        Task<List<TwitchAccount>> GetValidAccounts(int count);
        Task<List<TwitchAccount>> GetRandomValidAccounts(int count);
        Task UpdateAccount(TwitchAccount account);
        Task<List<TwitchAccount>> GetAll();
        Task AddAccount(TwitchAccount account);
        Task AddAccounts(List<TwitchAccount> accounts);
    }
}