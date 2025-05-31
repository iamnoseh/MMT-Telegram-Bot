using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TelegramBot.Domain.Entities;
using TelegramBot.Services;
using TelegramBot.Services.OptionServices;
using TelegramBot.Services.QuestionService;
using TelegramBot.Services.SubjectService;
using TelegramBot.Services.UserResponceService;

var builder = WebApplication.CreateBuilder(args);

// Register DbContext
builder.Services.AddDbContext<DataContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register services
builder.Services.AddScoped<IQuestionService, QuestionService>();
builder.Services.AddScoped<IOptionService, OptionService>();
builder.Services.AddScoped<IResponseService, ResponseService>();
builder.Services.AddScoped<ISubjectService, SubjectService>();

// Register TelegramBotHelper as a hosted service
builder.Services.AddHostedService<TelegramBotHostedService>();

var app = builder.Build();

// Perform database migration and seeding
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DataContext>();
    try
    {
        db.Database.Migrate();
        SeedSubjects(db);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error during database migration or seeding: {ex.Message}");
    }
}

// Map HTTP endpoint for questions
app.MapGet("/questions", async (DataContext context) =>
{
    var questions = await context.Questions.Include(q => q.Option).ToListAsync();
    return Results.Ok(questions);
});

await app.RunAsync();

void SeedSubjects(DataContext db)
{    var subjects = new[]
    {
        new Subject { Id = 1, Name = "Химия" },
        new Subject { Id = 2, Name = "Биология" },
        new Subject { Id = 3, Name = "Забони тоҷикӣ" },
        new Subject { Id = 4, Name = "English" },
        new Subject { Id = 5, Name = "Таърих" },
        new Subject { Id = 6, Name = "География" }
    };

    foreach (var subject in subjects)
    {
        if (!db.Subjects.Any(s => s.Id == subject.Id))
        {
            db.Subjects.Add(subject);
        }
    }

    db.SaveChanges();
}