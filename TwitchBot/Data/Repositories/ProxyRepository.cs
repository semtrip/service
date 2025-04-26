using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;
using TwitchViewerBot.Data;

namespace TwitchViewerBot.Data.Repositories
{
    public class ProxyRepository : IProxyRepository
    {
        private readonly AppDbContext _context;

        public ProxyRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<ProxyServer>> GetAll()
        {
            return await _context.Proxies.ToListAsync();
        }

        public async Task<List<ProxyServer>> GetValidProxies()
        {
            return await _context.Proxies
                .Where(p => p.IsValid)
                .ToListAsync();
        }

        public async Task<ProxyServer> GetById(int id)
        {
            return await _context.Proxies.FindAsync(id);
        }

        public async Task<int> BulkInsertProxies(IEnumerable<ProxyServer> proxies)
        {
            await _context.Proxies.AddRangeAsync(proxies);
            return await _context.SaveChangesAsync();
        }

        public async Task AddProxy(ProxyServer proxy)
        {
            Console.WriteLine(proxy);
            proxy.IsValid = false;
            proxy.LastChecked = DateTime.MinValue;

            await _context.Proxies.AddAsync(proxy);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateProxy(ProxyServer proxy)
        {
            // Получаем текущее состояние прокси из БД
            var existingProxy = await _context.Proxies.FindAsync(proxy.Id);
            if (existingProxy == null) return;

            // Обновляем только разрешенные поля
            existingProxy.Username = proxy.Username;
            existingProxy.Password = proxy.Password;
            existingProxy.LastChecked = proxy.LastChecked;

            // Явно НЕ обновляем IsValid и другие поля
            _context.Entry(existingProxy).State = EntityState.Modified;
            await _context.SaveChangesAsync();
        }

        public async Task DeleteProxy(int id)
        {
            var proxy = await GetById(id);
            if (proxy != null)
            {
                _context.Proxies.Remove(proxy);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<int> GetCount()
        {
            return await _context.Proxies.CountAsync();
        }
        public async Task<ProxyServer?> GetFreeProxy()
        {
            return await _context.Proxies
                .Where(p => p.IsValid && p.ActiveAccountsCount < 3)
                .OrderBy(p => p.ActiveAccountsCount)
                .FirstOrDefaultAsync();
        }
    }
}