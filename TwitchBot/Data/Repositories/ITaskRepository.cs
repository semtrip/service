using TwitchViewerBot.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TwitchViewerBot.Data.Repositories
{
    public interface ITaskRepository
    {
        Task<List<BotTask>> GetAll();
        Task<List<BotTask>> GetPendingTasks();
        Task<BotTask> GetById(int id);
        Task AddTask(BotTask task);
        Task UpdateTask(BotTask task);
    }
}