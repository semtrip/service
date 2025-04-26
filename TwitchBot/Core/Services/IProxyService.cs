using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchBot.Core.Models;

namespace TwitchBot.Core.Services
{
    public interface IProxyService
    {
        /// <summary>
        /// Загрузить прокси из файла, добавить новые и обновить существующие.
        /// </summary>
        Task<int> LoadOrUpdateProxiesFromFile(string filePath);

        /// <summary>
        /// Проверить и валидировать один прокси.
        /// </summary>
        Task<ProxyValidationResult> ValidateProxy(ProxyServer proxy);

        /// <summary>
        /// Проверить и валидировать все прокси из базы.
        /// </summary>
        Task<List<ProxyValidationResult>> ValidateAllProxies();

        /// <summary>
        /// Получить список валидных прокси из базы.
        /// </summary>
        Task<List<ProxyServer>> GetValidProxies();

        /// <summary>
        /// Добавить единичный прокси в базу.
        /// </summary>
        Task AddProxy(ProxyServer proxy);

        /// <summary>
        /// Обновить данные прокси в базе.
        /// </summary>
        Task UpdateProxy(ProxyServer proxy);

        /// <summary>
        /// Получить общее количество прокси в базе.
        /// </summary>
        Task<int> GetProxyCount();

        Task<bool> IsProxyValid(ProxyServer proxy);

        Task<ProxyServer> GetRandomValidProxy();
    }
}