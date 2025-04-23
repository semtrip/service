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
    .ConfigureLogging((context, logging) =>
    {
        // Очистка всех провайдеров логирования
        logging.ClearProviders();

        // Добавление консольного логирования
        logging.AddConsole();

        // Добавление файлового логирования
        logging.AddFile("logs/log.txt", LogLevel.Information);

        // Установка минимального уровня логирования
        logging.SetMinimumLevel(LogLevel.Debug);
    })
    .ConfigureServices((context, services) =>
    {
        // Регистрация сервисов
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite("Data Source=twitchbot.db"));

        services.AddScoped<ITwitchService, TwitchService>();
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
        services.AddScoped<TaskRunner>();

        // Регистрация UI
        services.AddScoped<MainMenu>();
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

        // Проверяем, пуста ли таблица прокси
        if (!await db.Proxies.AnyAsync())
        {
            // Инициализируем данные
            await DbInitializer.Initialize(db, proxyService, logger);
        }
        else
        {
            logger.LogInformation("Прокси уже загружены. Пропуск инициализации.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Ошибка при инициализации базы данных");
        throw;
    }
}

// Запуск главного меню
var mainMenu = host.Services.GetRequiredService<MainMenu>();
await mainMenu.ShowAsync();

await host.RunAsync();
