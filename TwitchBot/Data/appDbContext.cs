using Microsoft.EntityFrameworkCore;
using TwitchViewerBot.Core.Models;

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
            entity.Property(a => a.LastChecked).IsRequired();
        });

        modelBuilder.Entity<ProxyServer>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Address).IsRequired();
            entity.Property(p => p.Port).IsRequired();
            entity.Property(p => p.IsValid).IsRequired();
            entity.Property(p => p.LastChecked).IsRequired();
        });
    }
}
