using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TwitchViewerBot.ConsoleUI.Commands;
using TwitchViewerBot.ConsoleUI.Helpers;
using TwitchViewerBot.ConsoleUI.Menus;
using TwitchViewerBot.Core.Services;
using TwitchViewerBot.Data;
using TwitchViewerBot.Data.Repositories;
using TwitchViewerBot.Data.Seeders;
using TwitchViewerBot.Workers;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // Database
        services.AddDbContext<BotDbContext>(options =>
            options.UseSqlite("Data Source=twitchbot.db"));
        
        // Добавьте эту строку для AppDbContext
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite("Data Source=twitchbot.db"));

        // Services
        services.AddScoped<ITwitchService, TwitchService>();
        services.AddScoped<IProxyService, ProxyService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddSingleton<TaskMonitor>();

        // Repositories
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IProxyRepository, ProxyRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();

        // Commands
        services.AddTransient<ValidateProxiesCommand>();
        services.AddTransient<ValidateAccountsCommand>();
        services.AddTransient<StartTaskCommand>();
        services.AddTransient<ShowTasksCommand>();
        services.AddTransient<ShowLogsCommand>();
        services.AddTransient<ValidateTokensCommand>();

        // Workers
        services.AddHostedService<TaskRunner>();
        
        // UI
        services.AddSingleton<MainMenu>();
        services.AddSingleton<LoggingHelper>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
    });

var host = builder.Build();

// Initialize database
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
    await db.Database.EnsureDeletedAsync();
    await db.Database.EnsureCreatedAsync();
    await DbInitializer.Initialize(db);
}

// Start main menu
var mainMenu = host.Services.GetRequiredService<MainMenu>();
await mainMenu.ShowAsync();

await host.RunAsync();