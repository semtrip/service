using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchBot.Core.Models;

namespace TwitchBot.Data.Repositories
{
    public interface ITaskRepository
    {
        Task<List<BotTask>> GetAll();
        Task<List<BotTask>> GetPendingTasks();
        Task<List<BotTask>> GetRunningTasks();
        Task<BotTask?> GetById(int id);
        Task AddTask(BotTask task);
        Task UpdateTask(BotTask task);
    }
}