using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchBot.Core.Models;

namespace TwitchBot.Data.Repositories
{
    public class AccountRepository : IAccountRepository
    {
        private readonly AppDbContext _context;

        public AccountRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<TwitchAccount>> GetValidAccounts(int count)
        {
            return await _context.Accounts
                .Include(a => a.Proxy)
                .Where(a => a.IsValid && a.Proxy != null)
                .OrderBy(a => a.LastChecked)
                .Take(count)
                .ToListAsync();
        }
        public async Task<List<TwitchAccount>> GetRandomValidAccounts(int count)
        {
            return await _context.Accounts
                .Include(a => a.Proxy)
                .Where(a => a.IsValid && a.Proxy != null)
                .OrderBy(a => a.AuthToken)
                .Take(count)
                .ToListAsync();
        }


        public async Task UpdateAccount(TwitchAccount account)
        {
            account.LastChecked = DateTime.UtcNow;
            _context.Accounts.Update(account);
            await _context.SaveChangesAsync();
        }

        public async Task<List<TwitchAccount>> GetAll()
        {
            return await _context.Accounts.ToListAsync();
        }

        public async Task AddAccount(TwitchAccount account)
        {
            await _context.Accounts.AddAsync(account);
            await _context.SaveChangesAsync();
        }

        public async Task AddAccounts(List<TwitchAccount> accounts)
        {
            await _context.Accounts.AddRangeAsync(accounts);
            await _context.SaveChangesAsync();
        }
    }
}