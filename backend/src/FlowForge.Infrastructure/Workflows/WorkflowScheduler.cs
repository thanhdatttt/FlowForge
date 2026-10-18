using System.Text.Json;
using FlowForge.Domain.WorkItems;
using FlowForge.Domain.Workflows;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowForge.Infrastructure.Workflows;

// Scheduling has its own durable checkpoint; notification outbox acknowledgement cannot consume workflow events.
public sealed class WorkflowScheduler(FlowForgeDbContext db, TimeProvider clock, IOptions<WorkflowJobOptions> options,
    ILogger<WorkflowScheduler> logger)
{
    public async Task<bool> ScheduleNextAsync(CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var e = await db.DomainEvents.FromSqlRaw("""
            SELECT * FROM domain_events WHERE workflow_processed_at IS NULL
            ORDER BY occurred_at, id LIMIT 1 FOR UPDATE SKIP LOCKED
            """).SingleOrDefaultAsync(ct);
        if (e == null) return false;
        await WorkspaceLock.AcquireAsync(db, e.WorkspaceId, ct);
        if (e.ProjectId is Guid projectId && e.Depth < options.Value.MaxDepth && WorkflowDefinitionRules.Triggers.Contains(e.EventType))
        {
            var itemId = ItemId(e);
            if (await db.WorkItems.AnyAsync(x => x.Id == itemId && x.ProjectId == projectId && x.WorkspaceId == e.WorkspaceId, ct))
            {
                var matches = await (from w in db.Workflows
                    join v in db.WorkflowVersions on new { WorkflowId = w.Id, VersionNumber = w.CurrentVersion }
                        equals new { v.WorkflowId, v.VersionNumber }
                    where w.WorkspaceId == e.WorkspaceId && (w.ProjectId == null || w.ProjectId == projectId) &&
                        w.IsEnabled && w.EnabledAt <= e.OccurredAt && v.TriggerType == e.EventType
                    select new { Workflow = w, Version = v }).ToListAsync(ct);
                foreach (var match in matches)
                {
                    // Follow server-owned ancestry; a reused HTTP trace ID must not suppress unrelated events.
                    var workflowId = match.Workflow.Id;
                    if (await db.WorkflowExecutions.AnyAsync(x => x.WorkflowId == workflowId && x.EventId == e.Id, ct) ||
                        await HasAncestorAsync(workflowId, e.OriginExecutionId, ct)) continue;
                    var definition = JsonSerializer.Deserialize<WorkflowDefinition>(match.Version.Configuration)!;
                    var execution = new WorkflowExecution { WorkspaceId = e.WorkspaceId, ProjectId = projectId,
                        WorkItemId = itemId, WorkflowId = workflowId, WorkflowVersionId = match.Version.Id,
                        EventId = e.Id, CreatedAt = clock.GetUtcNow(), NextAttemptAt = clock.GetUtcNow() };
                    db.WorkflowExecutions.Add(execution);
                    foreach (var action in definition.Actions!)
                        db.WorkflowActionResults.Add(new WorkflowActionResult { ExecutionId = execution.Id, ActionId = action.Id });
                }
            }
        }
        e.WorkflowProcessedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
    private Guid ItemId(DomainEvent e)
    {
        if (e.EventType != "CommentAdded") return e.EntityId;
        try
        {
            using var json = JsonDocument.Parse(e.Payload);
            return json.RootElement.GetProperty("WorkItemId").GetGuid();
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // Poison payloads cannot starve the stream. Database failures still roll back and retry.
            logger.LogWarning("Workflow scheduling rejected event {EventId} ({ErrorType}).", e.Id, error.GetType().Name);
            return Guid.Empty;
        }
    }
    private async Task<bool> HasAncestorAsync(Guid workflowId, Guid? executionId, CancellationToken ct)
    {
        for (var depth = 0; executionId != null && depth < options.Value.MaxDepth; depth++)
        {
            var execution = await db.WorkflowExecutions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == executionId, ct);
            if (execution == null) return false;
            if (execution.WorkflowId == workflowId) return true;
            executionId = await db.DomainEvents.Where(x => x.Id == execution.EventId).Select(x => x.OriginExecutionId).SingleAsync(ct);
        }
        return executionId != null;
    }
}
