using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;
using TwitchViewerBot.Data.Repositories;

namespace TwitchViewerBot.Core.Services
{
    public class AccountService : IAccountService
    {
        private readonly IAccountRepository _accountRepository;

        public AccountService(IAccountRepository accountRepository)
        {
            _accountRepository = accountRepository;
        }

        public async Task<List<TwitchAccount>> GetValidAccounts(int count)
        {
            return await _accountRepository.GetValidAccounts(count);
        }
    }
}