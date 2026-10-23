using System.Data;
using FlowForge.Api.Contracts.Ai;
using FlowForge.Api.Validations.Ai;
using FlowForge.Application.Ai;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Ai;
using FlowForge.Infrastructure.Projects;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}/projects/{projectId:guid}/ai")
            .WithTags("AI").RequireAuthorization();
        group.MapPost("/tasks/propose", async (Guid workspaceId, Guid projectId, AiTaskProposalRequestDto request,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db,
            AiProviderCalls provider, AiPreviewTokens tokens, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            var errors = AiRequestValidator.Validate(request);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            var result = await provider.TasksAsync(new(workspaceId, projectId, project.Name, request.FeatureDescription!.Trim()), http, ct);
            db.ChangeTracker.Clear();
            var current = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(current.Project, current.Role);
            var preview = tokens.Create(workspaceId, projectId, ApiRules.UserId(http), result.Tasks!);
            return Results.Ok(new AiTaskPreviewDto(preview.Token, preview.ExpiresAt, result.Tasks!));
        }).RequireRateLimiting("ai");

        group.MapPost("/tasks/accept", async (Guid workspaceId, Guid projectId, AiTaskAcceptanceRequestDto request,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects,
            AiTaskAcceptance acceptance, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            var errors = AiRequestValidator.Validate(request);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            return Results.Ok(await acceptance.AcceptAsync(workspaceId, projectId, request, http, ct));
        });

        group.MapPost("/summary", async (Guid workspaceId, Guid projectId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db,
            AiProjectContextReader reader, AiProviderCalls provider, CancellationToken ct) =>
        {
            AiProjectSummaryContext context;
            await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
            {
                var (project, _) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
                context = await reader.ReadAsync(workspaceId, projectId, project.Name, ct);
                await snapshot.CommitAsync(ct);
            }
            AiProjectNarrative? narrative = null;
            if (context.Facts.TotalItems > 0) narrative = await provider.SummaryAsync(context, http, ct);
            db.ChangeTracker.Clear();
            await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            return Results.Ok(new AiProjectSummaryDto(projectId, context.AsOf, context.TodayUtc, context.Facts,
                narrative == null ? "InsufficientData" : "Generated", narrative, narrative != null, context.Items.Count, context.ContextTruncated));
        }).RequireRateLimiting("ai");
        return app;
    }
}
