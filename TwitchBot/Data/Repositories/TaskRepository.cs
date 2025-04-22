using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TwitchViewerBot.Core.Enums;
using TwitchViewerBot.Core.Models;
using TwitchViewerBot.Data;

namespace TwitchViewerBot.Data.Repositories
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
            await _context.Tasks.AddAsync(task);
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