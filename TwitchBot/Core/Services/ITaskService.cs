using TwitchViewerBot.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TwitchViewerBot.Core.Services
{
    public interface ITaskService
    {
        Task ProcessPendingTasks();
        Task StartTask(BotTask task);
        Task PauseTask(int taskId);
        Task ResumeTask(int taskId);
        Task<List<BotTask>> GetAllTasks();
        Task AddTask(BotTask task);
    }
}