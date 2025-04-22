using TwitchViewerBot.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace TwitchViewerBot.Data.Repositories
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

        public async Task UpdateAccount(TwitchAccount account)
        {
            account.LastChecked = DateTime.Now;
            _context.Accounts.Update(account);
            await _context.SaveChangesAsync();
        }
    }
}