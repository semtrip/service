using Microsoft.EntityFrameworkCore;
using TwitchViewerBot.Core.Models;

namespace TwitchViewerBot.Data
{
    public class BotDbContext : DbContext
    {
        public BotDbContext(DbContextOptions<BotDbContext> options) : base(options)
        {
        }

        public DbSet<TwitchAccount> Accounts { get; set; }
        public DbSet<ProxyServer> Proxies { get; set; }
        public DbSet<BotTask> Tasks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TwitchAccount>()
                .HasOne(a => a.Proxy)
                .WithMany()
                .HasForeignKey(a => a.ProxyId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}