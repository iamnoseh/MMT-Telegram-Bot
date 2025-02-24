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

// Configure Kestrel to listen on a specific IP address and port
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Any, 5000); // Барои гӯш кардани ҳама IP-ҳо дар порти 5000
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts(); // Барои ҳифзи SSL
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthorization();

// Танзими роутинг
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=ImportMvc}/{action=Index}/{id?}");

// Барои дастрасӣ танҳо бо HTTP (агар SSL сертификат истифода нашавад)
app.Run();