using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MMT.Application.Common.Interfaces.Repositories;
using MMT.Persistence.Contexts;
using MMT.Persistence.Repositories;

namespace MMT.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"));
        });
        
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        
        return services;
    }
}
