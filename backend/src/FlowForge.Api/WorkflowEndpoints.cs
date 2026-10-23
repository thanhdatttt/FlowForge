using System.Text.Json;
using FlowForge.Api.Contracts.Workflows;
using FlowForge.Api.Contracts.WorkItems;
using FlowForge.Api.Validations;
using FlowForge.Api.Validations.Workflows;
using FlowForge.Domain.Workflows;
using FlowForge.Domain.Workspaces;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Workflows;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}/workflows")
            .WithTags("Workflows").RequireAuthorization().AddEndpointFilter<WorkspaceMutationFilter>();
        group.MapGet("/", async (Guid workspaceId, Guid? projectId, int? page, int? pageSize, HttpContext http,
            WorkspaceAccessService access, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var role = await ApiRules.RoleAsync(workspaceId, http, access, ct);
            var errors = PaginationValidator.Validate(page, pageSize);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            var userId = ApiRules.UserId(http);
            var query = db.Workflows.AsNoTracking().Where(x => x.WorkspaceId == workspaceId);
            if (role is not (WorkspaceRole.Owner or WorkspaceRole.Admin))
                query = query.Where(x => role == WorkspaceRole.Manager && x.ProjectId != null &&
                    db.ProjectMemberships.Any(p => p.ProjectId == x.ProjectId && p.RemovedAt == null &&
                        p.WorkspaceMembership.UserId == userId && p.WorkspaceMembership.RemovedAt == null));
            if (projectId != null) query = query.Where(x => x.ProjectId == projectId);
            var count = await query.CountAsync(ct);
            var workflows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Skip(((page ?? 1) - 1) * (pageSize ?? 20)).Take(pageSize ?? 20).ToListAsync(ct);
            var items = new List<WorkflowDto>();
            foreach (var workflow in workflows) items.Add(await DtoAsync(db, workflow, ct));
            return Results.Ok(new PageDto<WorkflowDto>(items, count, page ?? 1, pageSize ?? 20));
        });
        group.MapPost("/validate", async (Guid workspaceId, WorkflowWriteDto request, HttpContext http,
            WorkflowAccess access, WorkflowReferences references, CancellationToken ct) =>
        {
            ApiRules.Require(await access.CanManageAsync(workspaceId, request.ProjectId, ApiRules.UserId(http), ct), 403, "You cannot manage workflows in this scope.");
            var errors = WorkflowWriteRequestValidator.Validate(request, false);
            if (errors.Count == 0) errors = await references.ValidateAsync(workspaceId, request.ProjectId, request.Definition!, ct);
            return errors.Count != 0 ? Results.ValidationProblem(errors) : Results.Ok(new { valid = true });
        });
        group.MapPost("/", async (Guid workspaceId, WorkflowWriteDto request, HttpContext http,
            WorkflowAccess access, WorkflowReferences references, FlowForgeDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            ApiRules.Require(await access.CanManageAsync(workspaceId, request.ProjectId, ApiRules.UserId(http), ct), 403, "You cannot manage workflows in this scope.");
            await WritableAsync(db, request.ProjectId, ct);
            var errors = WorkflowWriteRequestValidator.Validate(request, false);
            if (errors.Count == 0) errors = await references.ValidateAsync(workspaceId, request.ProjectId, request.Definition!, ct);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            var workflow = new Workflow { WorkspaceId = workspaceId, ProjectId = request.ProjectId,
                Name = request.Name!.Trim(), CreatedByUserId = ApiRules.UserId(http), CreatedAt = clock.GetUtcNow(), UpdatedAt = clock.GetUtcNow() };
            db.Workflows.Add(workflow);
            db.WorkflowVersions.Add(Version(workflow, request.Definition!, clock.GetUtcNow()));
            WorkItemSupport.Activity(db, workspaceId, workflow.ProjectId, workflow.CreatedByUserId, "workflow.created", "Workflow", workflow.Id,
                after: new { workflow.Name, workflow.CurrentVersion });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/workspaces/{workspaceId}/workflows/{workflow.Id}", await DtoAsync(db, workflow, ct));
        });
        group.MapGet("/{workflowId:guid}", async (Guid workspaceId, Guid workflowId, HttpContext http,
            WorkflowAccess access, FlowForgeDbContext db, CancellationToken ct) =>
            Results.Ok(await DtoAsync(db, await FindAsync(db, access, workspaceId, workflowId, ApiRules.UserId(http), ct), ct)));
        group.MapPut("/{workflowId:guid}", async (Guid workspaceId, Guid workflowId, WorkflowWriteDto request, HttpContext http,
            WorkflowAccess access, WorkflowReferences references, FlowForgeDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var workflow = await FindAsync(db, access, workspaceId, workflowId, ApiRules.UserId(http), ct);
            await WritableAsync(db, workflow.ProjectId, ct);
            var errors = WorkflowWriteRequestValidator.Validate(request, true);
            if (request.ProjectId != workflow.ProjectId) errors["projectId"] = ["Workflow scope cannot change."];
            if (errors.Count == 0) errors = await references.ValidateAsync(workspaceId, workflow.ProjectId, request.Definition!, ct);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            ApiRules.Require(request.Version == workflow.CurrentVersion, 409, "The workflow changed. Reload and retry.");
            var before = new { workflow.Name, workflow.CurrentVersion };
            workflow.Name = request.Name!.Trim(); workflow.CurrentVersion++; workflow.UpdatedAt = clock.GetUtcNow();
            db.WorkflowVersions.Add(Version(workflow, request.Definition!, clock.GetUtcNow()));
            WorkItemSupport.Activity(db, workspaceId, workflow.ProjectId, ApiRules.UserId(http), "workflow.updated", "Workflow", workflow.Id,
                before, new { workflow.Name, workflow.CurrentVersion });
            await db.SaveChangesAsync(ct);
            return Results.Ok(await DtoAsync(db, workflow, ct));
        });
        foreach (var enable in new[] { true, false })
            group.MapPost(enable ? "/{workflowId:guid}/enable" : "/{workflowId:guid}/disable",
                async (Guid workspaceId, Guid workflowId, WorkflowStateDto request, HttpContext http, WorkflowAccess access,
                    WorkflowReferences references, FlowForgeDbContext db, TimeProvider clock, CancellationToken ct) =>
            {
                var workflow = await FindAsync(db, access, workspaceId, workflowId, ApiRules.UserId(http), ct);
                var stateErrors = WorkflowStateRequestValidator.Validate(request);
                if (stateErrors.Count != 0) return Results.ValidationProblem(stateErrors);
                ApiRules.Require(request.Version == workflow.CurrentVersion, 409, "The workflow changed. Reload and retry.");
                if (enable)
                {
                    await WritableAsync(db, workflow.ProjectId, ct);
                    var definition = (await DtoAsync(db, workflow, ct)).Definition;
                    var errors = await references.ValidateAsync(workspaceId, workflow.ProjectId, definition, ct);
                    if (errors.Count != 0) return Results.ValidationProblem(errors);
                }
                if (workflow.IsEnabled != enable)
                {
                    workflow.IsEnabled = enable; workflow.UpdatedAt = clock.GetUtcNow();
                    if (enable) workflow.EnabledAt = clock.GetUtcNow();
                    else
                    {
                        var executions = await db.WorkflowExecutions.Where(x => x.WorkflowId == workflowId &&
                            (x.Status == "Pending" || x.Status == "Running")).ToListAsync(ct);
                        foreach (var execution in executions)
                            await WorkflowExecutionState.CancelAsync(db, execution, clock.GetUtcNow(), "Workflow disabled.", ct);
                    }
                    WorkItemSupport.Activity(db, workspaceId, workflow.ProjectId, ApiRules.UserId(http),
                        enable ? "workflow.enabled" : "workflow.disabled", "Workflow", workflow.Id, after: new { workflow.IsEnabled });
                    await db.SaveChangesAsync(ct);
                }
                return Results.Ok(await DtoAsync(db, workflow, ct));
            });
        group.MapGet("/executions/{executionId:guid}", async (Guid workspaceId, Guid executionId,
            HttpContext http, WorkflowAccess access, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var execution = await db.WorkflowExecutions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == executionId && x.WorkspaceId == workspaceId, ct);
            ApiRules.Require(execution != null, 404, "Resource not found.");
            await FindAsync(db, access, workspaceId, execution!.WorkflowId, ApiRules.UserId(http), ct);
            return Results.Ok(await ExecutionDtoAsync(db, execution, ct));
        });
        group.MapGet("/{workflowId:guid}/executions", async (Guid workspaceId, Guid workflowId, int? page, int? pageSize,
            HttpContext http, WorkflowAccess access, FlowForgeDbContext db, CancellationToken ct) =>
        {
            await FindAsync(db, access, workspaceId, workflowId, ApiRules.UserId(http), ct);
            var errors = PaginationValidator.Validate(page, pageSize);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            var query = db.WorkflowExecutions.AsNoTracking().Where(x => x.WorkflowId == workflowId);
            var count = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                .Skip(((page ?? 1) - 1) * (pageSize ?? 20)).Take(pageSize ?? 20).ToListAsync(ct);
            var result = new List<WorkflowExecutionDto>();
            foreach (var row in rows) result.Add(await ExecutionDtoAsync(db, row, ct));
            return Results.Ok(new PageDto<WorkflowExecutionDto>(result, count, page ?? 1, pageSize ?? 20));
        });
        group.MapGet("/{workflowId:guid}/executions/{executionId:guid}", async (Guid workspaceId, Guid workflowId, Guid executionId,
            HttpContext http, WorkflowAccess access, FlowForgeDbContext db, CancellationToken ct) =>
        {
            await FindAsync(db, access, workspaceId, workflowId, ApiRules.UserId(http), ct);
            var execution = await db.WorkflowExecutions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == executionId && x.WorkflowId == workflowId, ct);
            ApiRules.Require(execution != null, 404, "Resource not found.");
            return Results.Ok(await ExecutionDtoAsync(db, execution!, ct));
        });
    }

    private static async Task<Workflow> FindAsync(FlowForgeDbContext db, WorkflowAccess access, Guid workspaceId, Guid id, Guid userId, CancellationToken ct)
    {
        var workflow = await db.Workflows.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspaceId, ct);
        ApiRules.Require(workflow != null && await access.CanManageAsync(workspaceId, workflow.ProjectId, userId, ct), 404, "Resource not found.");
        return workflow!;
    }
    private static async Task WritableAsync(FlowForgeDbContext db, Guid? projectId, CancellationToken ct) =>
        ApiRules.Require(projectId == null || !await db.Projects.AnyAsync(x => x.Id == projectId && x.ArchivedAt != null, ct), 409, "Archived projects are read-only.");
    private static WorkflowVersion Version(Workflow workflow, WorkflowDefinition definition, DateTimeOffset now) =>
        new() { WorkflowId = workflow.Id, VersionNumber = workflow.CurrentVersion, TriggerType = definition.Trigger!,
            Configuration = JsonSerializer.Serialize(definition), CreatedAt = now };
    private static async Task<WorkflowDto> DtoAsync(FlowForgeDbContext db, Workflow workflow, CancellationToken ct)
    {
        var version = await db.WorkflowVersions.AsNoTracking().SingleAsync(x => x.WorkflowId == workflow.Id && x.VersionNumber == workflow.CurrentVersion, ct);
        return new(workflow.Id, workflow.WorkspaceId, workflow.ProjectId, workflow.Name, workflow.CreatedByUserId,
            workflow.IsEnabled, workflow.CurrentVersion, JsonSerializer.Deserialize<WorkflowDefinition>(version.Configuration)!, workflow.CreatedAt, workflow.UpdatedAt);
    }
    private static async Task<WorkflowExecutionDto> ExecutionDtoAsync(FlowForgeDbContext db, WorkflowExecution execution, CancellationToken ct)
    {
        var results = await db.WorkflowActionResults.AsNoTracking().Where(x => x.ExecutionId == execution.Id).ToListAsync(ct);
        var version = await db.WorkflowVersions.AsNoTracking().SingleAsync(x => x.Id == execution.WorkflowVersionId, ct);
        var definition = JsonSerializer.Deserialize<WorkflowDefinition>(version.Configuration)!;
        var source = await db.DomainEvents.AsNoTracking().SingleAsync(x => x.Id == execution.EventId, ct);
        var actions = definition.Actions!;
        var emails = await db.EmailDeliveries.AsNoTracking().Where(x => x.WorkflowExecutionId == execution.Id)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new WorkflowEmailDeliveryDto(x.Id, x.RecipientUserId, x.Status, x.AttemptCount, x.CompletedAt, x.SafeError)).ToListAsync(ct);
        return new(execution.Id, execution.WorkflowId, execution.WorkflowVersionId, execution.EventId, execution.ProjectId,
            execution.WorkItemId, execution.Status, execution.ConditionsMatched, execution.AttemptCount,
            execution.CreatedAt, execution.StartedAt, execution.FinishedAt, execution.SafeError,
            actions.Select(a => results.Single(x => x.ActionId == a.Id)).Select(x => new WorkflowActionResultDto(x.ActionId,
                x.Status, x.AttemptCount, x.FinishedAt, x.SafeError)).ToList(), emails, version.VersionNumber,
            definition, source.CorrelationId, source.ActorUserId, source.OriginExecutionId, source.Depth);
    }
}
