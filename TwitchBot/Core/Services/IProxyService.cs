using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Core.Services
{
    public interface IProxyService
    {
        Task<List<ProxyValidationResult>> ValidateAllProxies();
        Task<ProxyValidationResult> ValidateProxy(ProxyServer proxy);
        Task<List<ProxyServer>> GetValidProxies();
        Task<bool> TestProxyConnection(ProxyServer proxy);
        Task<int> LoadProxiesFromFile(string filePath);
        Task<int> GetProxyCount();
    }
}