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

        // Services
        services.AddScoped<ITwitchService, TwitchService>();
        services.AddScoped<IProxyService, ProxyService>();
        services.AddScoped<ITaskService, TaskService>();

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

        // Workers
        services.AddHostedService<TaskRunner>();
        services.AddHostedService<BotWorker>();

        // UI
        services.AddSingleton<MainMenu>();

        // Helpers
        services.AddSingleton<LoggingHelper>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
    });

var host = builder.Build();

// Initialize database
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
   
    // Удаляем существующую БД
    await db.Database.EnsureDeletedAsync();
    Console.WriteLine("База данных удалена");

    // Создаем новую
    await db.Database.EnsureCreatedAsync();
    Console.WriteLine("Новая база данных создана");

    await DbInitializer.Initialize(db);
}

// Start main menu
var mainMenu = host.Services.GetRequiredService<MainMenu>();
await mainMenu.ShowAsync();

await host.RunAsync();