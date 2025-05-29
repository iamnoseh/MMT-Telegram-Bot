using System.Net;
using data;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TelegramBot.Domain.Entities;

var builder = WebApplication.CreateBuilder(args);

// Танзим кардани DataContext ва дигар сервисҳо
builder.Services.AddDbContext<DataContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Танзим кардани Kestrel барои гӯш кардани танҳо localhost
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5049); // Гӯш кардани танҳо localhost дар порти 5049
});


builder.Services.AddControllersWithViews();

var app = builder.Build();

// --- Мигратсияҳоро ба базаи додаҳо автоматикунона татбиқ мекунад ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DataContext>();
    db.Database.Migrate();
}
// --- Мигратсияҳо анҷом ёфт ---

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts(); // Барои HTTPS дар муҳити истеҳсол
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// Танзими роутинг
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=ImportMvc}/{action=Index}/{id?}");

// Запуск сервер
app.Run();