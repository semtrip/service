using Microsoft.EntityFrameworkCore;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<ProxyServer> Proxies { get; set; }
        public DbSet<TwitchAccount> Accounts { get; set; }
        public DbSet<BotTask> Tasks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Настройка таблицы Proxies
            modelBuilder.Entity<ProxyServer>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.Property(p => p.Address).IsRequired();
                entity.Property(p => p.Port).IsRequired();
                entity.Property(p => p.Username).HasDefaultValue(string.Empty);
                entity.Property(p => p.Password).HasDefaultValue(string.Empty);
                entity.Property(p => p.IsValid).HasDefaultValue(false);
            });
        }
    }
}