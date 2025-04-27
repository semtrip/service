using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchBot.Core.Models;
using TwitchBot.Core.Enums;
using TwitchBot.Core.Models;

namespace TwitchBot.Core.Services
{
    public interface ITaskService
    {
        Task InitializeAsync();
        Task AddTask(BotTask task);
        Task StartTask(BotTask task);
        Task<List<BotTask>> GetAllTasks();
        Task<List<BotTask>> GetPendingTasks();
        Task<List<BotTask>> GetRunningTasks();
        Task ProcessPendingTasks();
        Task PauseTask(int taskId);
        Task ResumeTask(int taskId);
        Task CancelTask(int taskId);
        Task UpdateTask(BotTask task);
        Task AdjustViewers(BotTask task);
        Task CompleteTask(BotTask task);
        Task<BotTask?> GetById(int id);
        Task CancelAllTasks();
    }
}