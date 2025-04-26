using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Data.Repositories
{
    public interface IProxyRepository
    {
        /// <summary>
        /// Получает все прокси из базы
        /// </summary>
        Task<List<ProxyServer>> GetAll();

        /// <summary>
        /// Получает только валидные прокси
        /// </summary>
        Task<List<ProxyServer>> GetValidProxies();

        /// <summary>
        /// Находит прокси по ID
        /// </summary>
        Task<ProxyServer> GetById(int id);

        /// <summary>
        /// Массово добавляет прокси
        /// </summary>
        Task<int> BulkInsertProxies(IEnumerable<ProxyServer> proxies);

        /// <summary>
        /// Добавляет один прокси
        /// </summary>
        Task AddProxy(ProxyServer proxy);

        /// <summary>
        /// Обновляет данные прокси
        /// </summary>
        Task UpdateProxy(ProxyServer proxy);

        /// <summary>
        /// Удаляет прокси
        /// </summary>
        Task DeleteProxy(int id);

        /// <summary>
        /// Возвращает общее количество прокси
        /// </summary>
        Task<int> GetCount();

        Task<ProxyServer?> GetFreeProxy();
    }
}