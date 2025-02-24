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

// Танзим кардани Kestrel барои гӯш кардани танҳо IP-локалӣ (127.0.0.1) ва порти 5000
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Parse("31.130.144.99"), 5000); // IP-и хидматрасон ва порти 5000
});


builder.Services.AddControllersWithViews();

var app = builder.Build();

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