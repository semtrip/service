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
            options.UseSqlite("Data Source=twitchbot.db"));

        services.AddTransient<ITwitchService, TwitchService>();
        services.AddScoped<IProxyService, ProxyService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<TaskMonitor>();
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

    try
    {
        // Создаем БД, если не существует (без удаления)
        await db.Database.EnsureCreatedAsync();
        await DbInitializer.Initialize(db, proxyService, logger);
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
