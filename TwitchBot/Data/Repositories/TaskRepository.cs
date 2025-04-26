using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchBot.Core.Enums;
using TwitchBot.Core.Models;
using TwitchBot.Data;

namespace TwitchBot.Data.Repositories
{
    public class TaskRepository : ITaskRepository
    {
        private readonly AppDbContext _context;

        public TaskRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<BotTask>> GetAll()
        {
            return await _context.Tasks.ToListAsync();
        }

        public async Task<List<BotTask>> GetPendingTasks()
        {
            return await _context.Tasks
                .Where(t => t.Status == Core.Enums.TaskStatus.Pending)
                .ToListAsync();
        }

        public async Task<BotTask> GetById(int id)
        {
            return await _context.Tasks.FindAsync(id);
        }

        public async Task AddTask(BotTask task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            task.ErrorMessage = string.Empty;
            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateTask(BotTask task)
        {
            _context.Tasks.Update(task);
            await _context.SaveChangesAsync();
        }
        public async Task<List<BotTask>> GetRunningTasks()
        {
            return await _context.Tasks
                .Where(t => t.Status == Core.Enums.TaskStatus.Running)
                .ToListAsync();
        }
    }
}