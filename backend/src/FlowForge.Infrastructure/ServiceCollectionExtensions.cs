using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FlowForge.Infrastructure.Identity;
using FlowForge.Infrastructure.Workspaces;

namespace FlowForge.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // env reading service
        var connectionString = configuration.GetConnectionString("FlowForge");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:FlowForge is required. See backend/README.md for local setup.");
        }

        // services
        services.AddDbContext<FlowForgeDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<AuthService>();
        services.AddScoped<WorkspaceAccessService>();
        return services;
    }
}
