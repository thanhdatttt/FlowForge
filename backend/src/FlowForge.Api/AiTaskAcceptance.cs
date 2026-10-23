using System.Security.Cryptography;
using System.Text.Json;
using FlowForge.Api.Contracts.Ai;
using FlowForge.Application.Ai;
using FlowForge.Domain.WorkItems;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Projects;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

public sealed class AiTaskAcceptance(FlowForgeDbContext db, WorkspaceAccessService access,
    ProjectAccessService projects, AiPreviewTokens tokens, TimeProvider clock)
{
    public async Task<AiAcceptedTasksDto> AcceptAsync(Guid workspaceId, Guid projectId, AiTaskAcceptanceRequestDto request,
        HttpContext http, CancellationToken ct)
    {
        var userId = ApiRules.UserId(http);
        var preview = tokens.Read(request.PreviewToken!, workspaceId, projectId, userId);
        ApiRules.Require(request.Tasks!.All(x => preview.ProposalIds.Contains(x.Id!, StringComparer.Ordinal)),
            400, "Tasks must come from this preview.");
        var tasks = request.Tasks!.Select(x => x with { Title = x.Title!.Trim(), Description = x.Description?.Trim() }).ToList();
        var canonical = tasks.OrderBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => x with { DependsOn = x.DependsOn!.OrderBy(id => id, StringComparer.Ordinal).ToArray() });
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical)));

        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        ApiRules.Require(await WorkspaceLock.AcquireAsync(db, workspaceId, ct) != null, 404, "Resource not found.");
        var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
        WorkItemSupport.Write(project, role);
        // The workspace lock serializes confirmation and ordinary writes; the receipt commits with every effect.
        var existing = await db.ActivityRecords.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId &&
            x.ProjectId == projectId && x.Action == "ai.tasks.accepted" && x.EntityType == "AiTaskBatch" && x.EntityId == preview.BatchId, ct);
        if (existing != null)
        {
            var receipt = JsonSerializer.Deserialize<AcceptanceReceipt>(existing.AfterValues!)!;
            ApiRules.Require(receipt.RequestHash == hash, 409, "This preview was already accepted with different tasks.");
            await transaction.CommitAsync(ct);
            return new(preview.BatchId, receipt.Tasks);
        }
        var reporter = await WorkItemSupport.MemberAsync(db, workspaceId, projectId, userId, ct);
        var status = await WorkItemSupport.StatusAsync(db, projectId, "Todo", ct);
        var now = clock.GetUtcNow();
        var items = new Dictionary<string, WorkItem>(StringComparer.Ordinal);
        foreach (var task in tasks)
        {
            var item = new WorkItem { WorkspaceId = workspaceId, ProjectId = projectId, ItemNumber = project.NextItemNumber++,
                Title = task.Title!, Description = task.Description, Type = "Task", StatusId = status.Id,
                Priority = task.Priority!, ReporterMembershipId = reporter!.Id, CreatedAt = now, UpdatedAt = now };
            db.WorkItems.Add(item); items.Add(task.Id!, item);
            WorkItemSupport.Activity(db, workspaceId, projectId, userId, "work-item.created", "WorkItem", item.Id,
                after: new { item.ItemNumber, item.Title, item.Description, item.Type, Status = status.Name,
                    item.Priority, AiBatchId = preview.BatchId, ProposalId = task.Id });
            WorkItemSupport.EmitEvent(db, workspaceId, projectId, userId, "WorkItemCreated", item.Id,
                new { item.Id, item.ItemNumber, item.StatusId, item.AssigneeMembershipId, item.DueDate });
        }
        foreach (var task in tasks)
            foreach (var dependency in task.DependsOn!)
            {
                var item = items[task.Id!];
                var target = items[dependency];
                db.WorkItemDependencies.Add(new WorkItemDependency { ProjectId = projectId,
                    WorkItemId = item.Id, DependsOnItemId = target.Id, CreatedAt = now });
                WorkItemSupport.Activity(db, workspaceId, projectId, userId, "dependency.added", "WorkItem", item.Id,
                    after: new { DependsOnItemId = target.Id, AiBatchId = preview.BatchId });
            }
        var created = tasks.Select(x => new AiCreatedTaskDto(x.Id!, items[x.Id!].Id, $"{project.ProjectKey}-{items[x.Id!].ItemNumber}")).ToList();
        WorkItemSupport.Activity(db, workspaceId, projectId, userId, "ai.tasks.accepted", "AiTaskBatch", preview.BatchId,
            after: new AcceptanceReceipt(hash, created));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(preview.BatchId, created);
    }

    private sealed record AcceptanceReceipt(string RequestHash, IReadOnlyList<AiCreatedTaskDto> Tasks);
}
