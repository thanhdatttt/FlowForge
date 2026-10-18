using System.Text.Json;
using FlowForge.Domain.WorkItems;
using FlowForge.Domain.Workflows;
using FlowForge.Infrastructure.Notifications;
using FlowForge.Infrastructure.WorkItems;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowForge.Infrastructure.Workflows;

public sealed class WorkflowRunner(FlowForgeDbContext db, WorkflowAccess access, WorkflowActions actions,
    TimeProvider clock, IOptions<WorkflowJobOptions> options, ILogger<WorkflowRunner> logger)
{
    public async Task<bool> RunNextAsync(CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        WorkflowExecution? execution;
        var max = options.Value.MaxAttempts;
        var now = clock.GetUtcNow();
        var exhausted = false;
        await using (var claim = await db.Database.BeginTransactionAsync(ct))
        {
            execution = await db.WorkflowExecutions.FromSqlInterpolated($"""
                SELECT * FROM workflow_executions WHERE status IN ('Pending', 'Running') AND next_attempt_at <= {now}
                ORDER BY next_attempt_at, id LIMIT 1 FOR UPDATE SKIP LOCKED
                """).SingleOrDefaultAsync(ct);
            if (execution == null) return false;
            execution.LockId = Guid.NewGuid(); execution.Status = "Running";
            execution.NextAttemptAt = now.AddMinutes(2); execution.StartedAt ??= now;
            exhausted = execution.AttemptCount >= max;
            if (!exhausted) execution.AttemptCount++;
            await db.SaveChangesAsync(ct); await claim.CommitAsync(ct);
        }
        var id = execution.Id;
        var lockId = execution.LockId;
        Guid? actionId = null;
        try
        {
            // Each action has its own transaction, permitting disable/revocation between actions.
            // Its business effects, completion ledger, audit and emitted events commit atomically.
            while (true)
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await WorkspaceLock.AcquireAsync(db, execution.WorkspaceId, ct);
                var owned = await db.WorkflowExecutions.FromSqlInterpolated($"""
                    SELECT * FROM workflow_executions WHERE id = {id} AND lock_id = {lockId}
                    AND status = 'Running' AND next_attempt_at > {clock.GetUtcNow()} FOR UPDATE
                    """).SingleOrDefaultAsync(ct);
                if (owned == null) return true;
                execution = owned;
                var workflow = await db.Workflows.AsNoTracking().SingleAsync(x => x.Id == execution.WorkflowId, ct);
                var item = await db.WorkItems.SingleAsync(x => x.Id == execution.WorkItemId && x.ProjectId == execution.ProjectId, ct);
                if (!workflow.IsEnabled || item.ArchivedAt != null ||
                    !await access.CanExecuteAsync(execution.WorkspaceId, execution.ProjectId, workflow.CreatedByUserId, ct))
                {
                    await WorkflowExecutionState.CancelAsync(db, execution, clock.GetUtcNow(), "Workflow disabled or current resource access revoked.", ct);
                    await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return true;
                }
                var version = await db.WorkflowVersions.AsNoTracking().SingleAsync(x => x.Id == execution.WorkflowVersionId, ct);
                var definition = JsonSerializer.Deserialize<WorkflowDefinition>(version.Configuration)!;
                if (WorkflowDefinitionRules.Validate(definition).Count != 0) throw new WorkResourceFailure("Invalid workflow configuration.");
                var source = await db.DomainEvents.AsNoTracking().SingleAsync(x => x.Id == execution.EventId, ct);
                if (execution.ConditionsMatched == null)
                {
                    if (exhausted) throw new WorkResourceFailure("Workflow attempt limit reached.");
                    var status = await db.ProjectStatuses.Where(x => x.Id == item.StatusId).Select(x => x.Name).SingleAsync(ct);
                    var assignee = await db.WorkspaceMemberships.Where(x => x.Id == item.AssigneeMembershipId).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
                    var labels = await (from link in db.WorkItemLabels join label in db.WorkLabels on link.LabelId equals label.Id
                        where link.WorkItemId == item.Id select label.Name).ToListAsync(ct);
                    var context = new WorkflowConditionContext(item.Priority, status, item.Type, assignee, source.ActorUserId,
                        labels, item.DueDate, item.Title, item.Description);
                    execution.ConditionsMatched = definition.Conditions!.All(c => WorkflowConditions.Matches(c, context));
                    await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
                    continue;
                }
                var results = await db.WorkflowActionResults.Where(x => x.ExecutionId == id).ToListAsync(ct);
                var action = execution.ConditionsMatched == true
                    ? definition.Actions!.FirstOrDefault(a => results.Single(r => r.ActionId == a.Id).Status != "Succeeded") : null;
                if (action == null)
                {
                    execution.Status = "Succeeded"; execution.FinishedAt = clock.GetUtcNow(); execution.LockId = null;
                    foreach (var result in results.Where(x => x.Status == "Pending"))
                    { result.Status = "Cancelled"; result.SafeError = "Conditions did not match."; result.FinishedAt = clock.GetUtcNow(); }
                    await WorkflowExecutionState.RecordAsync(db, execution, clock.GetUtcNow(), ct);
                    await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return true;
                }
                if (exhausted) throw new WorkResourceFailure("Workflow attempt limit reached.");
                actionId = action.Id;
                await actions.ExecuteAsync(workflow, execution, source, item, action, ct);
                var completed = results.Single(x => x.ActionId == action.Id);
                completed.Status = "Succeeded"; completed.AttemptCount++; completed.FinishedAt = clock.GetUtcNow(); completed.SafeError = null;
                execution.NextAttemptAt = clock.GetUtcNow().AddMinutes(2); execution.SafeError = null;
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
                actionId = null;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await WorkspaceLock.AcquireAsync(db, execution.WorkspaceId, ct);
            var owned = await db.WorkflowExecutions.SingleOrDefaultAsync(x => x.Id == id && x.LockId == lockId && x.Status == "Running", ct);
            if (owned == null) return true;
            var transient = error is DbUpdateException { InnerException: { } inner }
                ? NotificationRetry.IsTransient(inner) : NotificationRetry.IsTransient(error);
            var terminal = !transient || owned.AttemptCount >= max;
            owned.Status = terminal ? "Failed" : "Pending"; owned.LockId = null;
            owned.NextAttemptAt = clock.GetUtcNow().Add(NotificationRetry.Delay(owned.AttemptCount));
            owned.SafeError = error is WorkResourceFailure ? error.Message : terminal ? "Permanent workflow failure." : "Transient workflow failure.";
            if (terminal) owned.FinishedAt = clock.GetUtcNow();
            if (actionId is Guid action)
            {
                var result = await db.WorkflowActionResults.SingleAsync(x => x.ExecutionId == id && x.ActionId == action, ct);
                result.AttemptCount++; result.SafeError = owned.SafeError;
                if (terminal) { result.Status = "Failed"; result.FinishedAt = clock.GetUtcNow(); }
            }
            if (terminal)
            {
                foreach (var result in await db.WorkflowActionResults.Where(x => x.ExecutionId == id && x.Status == "Pending" && x.ActionId != actionId).ToListAsync(ct))
                { result.Status = "Cancelled"; result.FinishedAt = clock.GetUtcNow(); result.SafeError = "Execution failed."; }
                await WorkflowExecutionState.RecordAsync(db, owned, clock.GetUtcNow(), ct);
            }
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            logger.LogWarning("Workflow execution {ExecutionId} attempt {Attempt} failed ({ErrorType}); terminal: {Terminal}.",
                id, owned.AttemptCount, error.GetType().Name, terminal);
        }
        return true;
    }
}
