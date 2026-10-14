using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FlowForge.Infrastructure.Identity;

namespace FlowForge.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FlowForge");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:FlowForge is required. See backend/README.md for local setup.");
        }

        services.AddDbContext<FlowForgeDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<AuthService>();
        return services;
    }
}
