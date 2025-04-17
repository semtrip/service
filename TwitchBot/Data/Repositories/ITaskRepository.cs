using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Data.Repositories
{
    public interface ITaskRepository
    {
        Task<List<BotTask>> GetAll();
        Task<List<BotTask>> GetPendingTasks();
        Task<List<BotTask>> GetRunningTasks(); // Добавьте этот метод
        Task<BotTask?> GetById(int id);
        Task AddTask(BotTask task);
        Task UpdateTask(BotTask task);
    }
}