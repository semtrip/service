using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Data.Repositories
{
    public interface IProxyRepository
    {
        Task<List<ProxyServer>> GetAll();
        Task<List<ProxyServer>> GetValidProxies();
        Task<ProxyServer> GetById(int id);
        Task<int> BulkInsertProxies(IEnumerable<ProxyServer> proxies);
        Task AddProxy(ProxyServer proxy);
        Task UpdateProxy(ProxyServer proxy);
        Task DeleteProxy(int id);
        Task<int> GetCount();
    }
}