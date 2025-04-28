using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchBot.ConsoleUI.Commands;
using TwitchBot.ConsoleUI.Helpers;
using TwitchBot.ConsoleUI.Menus;
using TwitchBot.Core.Services;
using TwitchBot.Data.Repositories;
using TwitchBot.Data.Seeders;
using TwitchBot.Data;
using TwitchBot.Workers;
using TwitchBot.ConsoleUI.Commands;
using TwitchBot.ConsoleUI.Menus;
using TwitchBot.Core.Services;
using TwitchBot.Data;
using TwitchBot.Data.Repositories;
using TwitchBot.Data.Seeders;
using TwitchBot.Workers;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(context.Configuration.GetConnectionString("DefaultConnection")));

        // Регистрация репозиториев
        services.AddScoped<IProxyRepository, ProxyRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();

        // Регистрация сервисов
        services.AddScoped<IAccountValidator, AccountValidator>();
        services.AddSingleton<WebDriverPool>(_ => new WebDriverPool(40));
        services.AddSingleton<ITwitchService, TwitchService>();
        services.AddSingleton<IProxyService, ProxyService>();
        services.AddSingleton<IAccountService, AccountService>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<TaskMonitor>();
        services.AddSingleton<LoggingHelper>();
        services.AddSingleton<ILoggerProvider, LoggerProvider>();

        // Регистрация команд
        services.AddTransient<ValidateProxiesCommand>();
        services.AddTransient<ValidateAccountsCommand>();
        services.AddTransient<StartTaskCommand>();
        services.AddTransient<ShowTasksCommand>();
        services.AddTransient<ShowLogsCommand>();
        services.AddTransient<ValidateTokensCommand>();

        // Регистрация воркеров
        services.AddHostedService<TaskRunner>();

        // Регистрация UI
        services.AddScoped<MainMenu>();

        services.AddHttpClient("twitch")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                MaxConnectionsPerServer = 100
            });
        services.AddHttpClient("twitch_light", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
        });
    });

using var host = builder.Build();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
var driverPool = host.Services.GetRequiredService<WebDriverPool>();

var cts = new CancellationTokenSource();
lifetime.ApplicationStopping.Register(() =>
{
    cts.Cancel();
    driverPool.Dispose();
});

try
{
    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var proxyService = scope.ServiceProvider.GetRequiredService<IProxyService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<DbInitializer>>();
        var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();

        await db.Database.EnsureCreatedAsync();
        await DbInitializer.Initialize(db, proxyService, taskService, logger);
        await taskService.InitializeAsync();
    }

    await host.StartAsync(cts.Token);

    var mainMenu = host.Services.GetRequiredService<MainMenu>();
    await mainMenu.ShowAsync();
}
catch (OperationCanceledException)
{
    Console.WriteLine("Application shutdown requested");
}
finally
{
    if (!cts.IsCancellationRequested)
    {
        cts.Cancel();
    }

    await host.StopAsync();
    host.Dispose();
}