using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TwitchViewerBot.Core.Models;

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
            await _context.Proxies.AddAsync(proxy);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateProxy(ProxyServer proxy)
        {
            _context.Proxies.Update(proxy);
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
    }
}