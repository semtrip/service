//Program.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TwitchBot.ConsoleUI.Commands;
using TwitchBot.ConsoleUI.Helpers;
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
        logging.AddProvider(new AdvancedLoggerProvider());
        logging.AddConsole();
        logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
    })
    .ConfigureServices((context, services) =>
    {
        // Регистрация сервисов
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(context.Configuration.GetConnectionString("DefaultConnection")));

        services.AddSingleton<ITwitchService, TwitchService>();
        services.AddSingleton<IProxyService, ProxyService>();
        services.AddSingleton<IAccountService, AccountService>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<TaskMonitor>();
        services.AddSingleton<LoggingHelper>();
        services.AddSingleton<ILoggerProvider, LoggerProvider>();

        // Регистрация репозиториев
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IProxyRepository, ProxyRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();

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
        services.AddSingleton<WebDriverPool>(_ => new WebDriverPool(
            maxDrivers: 40, // Оптимальное количество драйверов
            proxy: null));
    });

var host = builder.Build();

// Инициализация базы данных
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var proxyService = scope.ServiceProvider.GetRequiredService<IProxyService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<DbInitializer>>();
    var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();

    try
    {
        // Создаем БД, если не существует (без удаления)
        await db.Database.EnsureCreatedAsync();
        await DbInitializer.Initialize(db, proxyService, logger);
        await taskService.InitializeAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Ошибка при инициализации базы данных");
        throw;
    }
}

var runHost = host.RunAsync();

// Параллельно запускаем меню
var mainMenu = host.Services.GetRequiredService<MainMenu>();
await mainMenu.ShowAsync();


// Дождаться завершения хоста
await runHost;
