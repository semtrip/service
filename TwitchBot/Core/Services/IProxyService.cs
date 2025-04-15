using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Core.Services
{
    public interface IProxyService
    {
        Task<List<ProxyValidationResult>> ValidateAllProxies();
        Task<bool> TestProxy(ProxyServer proxy);
        Task<List<ProxyServer>> GetValidProxies();
    }
}