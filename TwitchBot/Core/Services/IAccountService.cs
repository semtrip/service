using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchBot.Core.Models;

namespace TwitchBot.Core.Services
{
    public interface IAccountService
    {
        Task<List<TwitchAccount>> GetValidAccounts(int count);
        Task UpdateAccount(TwitchAccount account);
        Task ValidateAccounts();
    }
}