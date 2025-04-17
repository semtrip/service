using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Core.Services
{
    public interface ITaskService
    {
        Task AddTask(BotTask task);
        Task<List<BotTask>> GetAllTasks();
        Task<List<BotTask>> GetPendingTasks();
        Task<List<BotTask>> GetRunningTasks();
        Task ProcessPendingTasks();
        Task StartTask(BotTask task);
        Task PauseTask(int taskId);
        Task ResumeTask(int taskId);
        Task CancelTask(int taskId);
        Task UpdateTask(BotTask task);
    }
}