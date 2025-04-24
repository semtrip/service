// TwitchBot/Core/Services/ITaskService.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Enums;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Core.Services
{
    public interface ITaskService
    {
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
    }
}