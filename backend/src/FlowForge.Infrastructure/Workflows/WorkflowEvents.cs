using System.Text.Json;
using FlowForge.Domain.WorkItems;
using FlowForge.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Infrastructure.Workflows;

public static class WorkflowEvents
{
    public static void Emit(FlowForgeDbContext db, WorkflowExecution execution, DomainEvent source, Guid creator,
        string type, Guid entityId, object payload, DateTimeOffset now)
    {
        var e = new DomainEvent { WorkspaceId = execution.WorkspaceId, ProjectId = execution.ProjectId,
            ActorUserId = creator, EventType = type, EntityId = entityId, Payload = JsonSerializer.Serialize(payload),
            CorrelationId = source.CorrelationId, OriginExecutionId = execution.Id, Depth = source.Depth + 1, OccurredAt = now };
        db.DomainEvents.Add(e);
        db.OutboxMessages.Add(new OutboxMessage { EventId = e.Id, MessageType = "DomainEvent", AvailableAt = now });
    }
}

public static class WorkflowExecutionState
{
    public static async Task CancelAsync(FlowForgeDbContext db, WorkflowExecution execution, DateTimeOffset now, string reason, CancellationToken ct)
    {
        execution.Status = "Cancelled"; execution.FinishedAt = now; execution.SafeError = reason; execution.LockId = null;
        foreach (var result in await db.WorkflowActionResults.Where(x => x.ExecutionId == execution.Id && x.Status == "Pending").ToListAsync(ct))
        { result.Status = "Cancelled"; result.FinishedAt = now; result.SafeError = reason; }
        await RecordAsync(db, execution, now, ct);
    }
    public static async Task RecordAsync(FlowForgeDbContext db, WorkflowExecution execution, DateTimeOffset now, CancellationToken ct)
    {
        var workflow = await db.Workflows.AsNoTracking().SingleAsync(x => x.Id == execution.WorkflowId, ct);
        var source = await db.DomainEvents.AsNoTracking().SingleAsync(x => x.Id == execution.EventId, ct);
        db.ActivityRecords.Add(new ActivityRecord { WorkspaceId = execution.WorkspaceId, ProjectId = execution.ProjectId,
            WorkItemId = execution.WorkItemId, ActorUserId = workflow.CreatedByUserId, ActorKind = "System",
            Action = "workflow." + execution.Status.ToLowerInvariant(), EntityType = "WorkflowExecution", EntityId = execution.Id,
            WorkflowExecutionId = execution.Id, CorrelationId = source.CorrelationId, OccurredAt = now,
            AfterValues = JsonSerializer.Serialize(new { execution.Status, execution.ConditionsMatched, execution.SafeError }) });
        WorkflowEvents.Emit(db, execution, source, workflow.CreatedByUserId, "WorkflowExecutionUpdated", execution.Id,
            new { execution.Id, execution.Status }, now);
    }
}
