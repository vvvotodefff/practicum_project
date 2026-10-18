using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Users.Application.Abstractions;
using ProjectWork.Users.Application.Abstractions.Repositories;
using ProjectWork.Users.Infrastructure.Persistence;
using ProjectWork.Users.Infrastructure.Repositories;
using ProjectWork.Users.Infrastructure.Security;

namespace ProjectWork.Users.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Не задана строка подключения DefaultConnection.");
        services.AddDbContext<UsersDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSecurityServices(configuration);
        return services;
    }
}
