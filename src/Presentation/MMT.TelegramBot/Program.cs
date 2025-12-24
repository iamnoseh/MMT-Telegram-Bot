using MMT.Application;
using MMT.Persistence;
using MMT.TelegramBot.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

ConfigureLogging(builder);
ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

await InitializeDatabaseAsync(app.Services);

app.Run();

static void ConfigureLogging(WebApplicationBuilder builder)
{
    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            path: "logs/bot-.txt",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    builder.Host.UseSerilog();
    
    Log.Information("Application starting up...");
}

static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    services.AddApplication();
    services.AddPersistence(configuration);
    
    services.AddHostedService<TelegramBotHostedService>();
    
    services.AddHealthChecks();
}

static async Task InitializeDatabaseAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MMT.Persistence.Contexts.ApplicationDbContext>();
        
        await context.Database.EnsureCreatedAsync();
        
        Log.Information("Database initialized successfully");
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "An error occurred while initializing the database");
        throw;
    }
}
