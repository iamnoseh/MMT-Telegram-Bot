using MMT.Application;
using MMT.Persistence;
using MMT.TelegramBot.Services;
using Microsoft.EntityFrameworkCore;
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
    
    services.Configure<MMT.TelegramBot.Configuration.BotConfiguration>(
        configuration.GetSection(MMT.TelegramBot.Configuration.BotConfiguration.SectionName));
    
    services.AddHostedService<TelegramBotHostedService>();
    
    services.AddHealthChecks();
}

static async Task InitializeDatabaseAsync(IServiceProvider services)
{
    try
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MMT.Persistence.Contexts.ApplicationDbContext>();
        
        Log.Information("Applying database migrations...");
        await context.Database.MigrateAsync();
        Log.Information("Database migrations applied successfully");
        
        if (!await context.Subjects.AnyAsync())
        {
            Log.Information("Seeding subjects...");
            var subjects = new[]
            {
                new MMT.Domain.Entities.Subject { Name = "Химия" },
                new MMT.Domain.Entities.Subject { Name = "Биология" },
                new MMT.Domain.Entities.Subject { Name = "Забони тоҷикӣ" },
                new MMT.Domain.Entities.Subject { Name = "English" },
                new MMT.Domain.Entities.Subject { Name = "Таърих" },
                new MMT.Domain.Entities.Subject { Name = "География" },
                new MMT.Domain.Entities.Subject { Name = "Адабиёти тоҷик" },
                new MMT.Domain.Entities.Subject { Name = "Физика" },
                new MMT.Domain.Entities.Subject { Name = "Забони русӣ" },
                new MMT.Domain.Entities.Subject { Name = "Математика" },
                new MMT.Domain.Entities.Subject { Name = "Анатомия" },
                new MMT.Domain.Entities.Subject { Name = "Ҳуқуқи инсон" },
                new MMT.Domain.Entities.Subject { Name = "Генетика" }
            };
            
            await context.Subjects.AddRangeAsync(subjects);
            await context.SaveChangesAsync();
            Log.Information("Successfully seeded {Count} subjects", subjects.Length);
        }
        else
        {
            Log.Information("Subjects already exist, skipping seeding");
        }
        
        var superAdmin = await context.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == "iamnoseh");
        
        if (superAdmin != null && !superAdmin.IsAdmin)
        {
            Log.Information("Setting super admin for user: {Username}", superAdmin.Username);
            superAdmin.IsAdmin = true;
            context.Users.Update(superAdmin);
            await context.SaveChangesAsync();
            Log.Information("Super admin set successfully");
        }
        else if (superAdmin != null)
        {
            Log.Information("Super admin already configured");
        }
        
        Log.Information("Database initialized successfully");
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "An error occurred while initializing the database");
        throw;
    }
}
