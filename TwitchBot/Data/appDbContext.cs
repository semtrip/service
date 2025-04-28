using Microsoft.EntityFrameworkCore;
using TwitchBot.Core.Models;

namespace TwitchBot.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<TwitchAccount> Accounts { get; set; }
        public DbSet<ProxyServer> Proxies { get; set; }
        public DbSet<BotTask> Tasks { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TwitchAccount>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Username).IsRequired();
                entity.Property(a => a.AuthToken).IsRequired();
                entity.Property(a => a.IsValid).IsRequired();
                entity.Property(a => a.LastChecked)
                    .IsRequired()
                    .HasConversion(
                        v => v.ToUniversalTime(),
                        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
                entity.Property(a => a.Cookies);
            });

            modelBuilder.Entity<ProxyServer>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.Property(p => p.Address).IsRequired();
                entity.Property(p => p.Port).IsRequired();
                entity.Property(p => p.IsValid).IsRequired();
                entity.Property(p => p.LastChecked)
                    .IsRequired()
                    .HasConversion(
                        v => v.ToUniversalTime(),
                        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            });

            modelBuilder.Entity<BotTask>(entity =>
            {
                entity.HasKey(t => t.Id);
                entity.Property(t => t.ChannelUrl).IsRequired();
                entity.Property(t => t.Status).IsRequired();
                entity.Property(t => t.LastUpdated)
                    .HasConversion(
                        v => v.ToUniversalTime(),
                        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

                entity.Property(t => t.StartTime)
                    .HasConversion(
                        v => v.HasValue ? v.Value.ToUniversalTime() : (DateTime?)null,
                        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null);

                entity.Property(t => t.EndTime)
                    .HasConversion(
                        v => v.HasValue ? v.Value.ToUniversalTime() : (DateTime?)null,
                        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null);
            });
        }
    }
}