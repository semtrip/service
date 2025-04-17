using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Core.Services
{
    public interface IAccountService
    {
        Task<List<TwitchAccount>> GetValidAccounts(int count);
    }
}