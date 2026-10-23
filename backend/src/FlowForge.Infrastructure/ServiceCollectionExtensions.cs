using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FlowForge.Infrastructure.Identity;
using FlowForge.Infrastructure.Workspaces;
using FlowForge.Infrastructure.Projects;
using FlowForge.Infrastructure.Notifications;
using FlowForge.Infrastructure.Workflows;
using FlowForge.Infrastructure.Ai;
using FlowForge.Application.Ai;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlowForge.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // connection string reading
        var connectionString = configuration.GetConnectionString("FlowForge");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:FlowForge is required. See backend/README.md for local setup.");
        }

        // services
        services.AddDbContext<FlowForgeDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<Application.Email.IEmailSender, Email.GmailSmtpEmailSender>();
        services.AddScoped<AuthService>();
        services.AddScoped<WorkspaceAccessService>();
        services.AddScoped<ProjectAccessService>();
        services.AddScoped<NotificationAccess>();
        services.AddScoped<NotificationQueue>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<EmailDeliveryProcessor>();
        services.AddScoped<OverdueChecker>();
        services.AddScoped<WorkflowAccess>();
        services.AddScoped<WorkflowReferences>();
        services.AddScoped<WorkflowScheduler>();
        services.AddScoped<WorkflowActions>();
        services.AddScoped<WorkflowRunner>();
        services.AddScoped<WorkflowDateScanner>();
        services.TryAddScoped<IAiProvider>(provider => provider.GetRequiredService<OllamaCloudAiProvider>());
        services.AddScoped<AiProjectContextReader>();
        return services;
    }
}
