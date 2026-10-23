using System.Text.Json;
using FlowForge.Api.Contracts.WorkItems;
using FlowForge.Domain.Projects;
using FlowForge.Domain.WorkItems;
using FlowForge.Domain.Workspaces;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Projects;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

internal static partial class WorkItemSupport
{
    public static async Task<(Project Project, WorkspaceRole Role)> ProjectAsync(Guid workspaceId, Guid projectId,
        HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, CancellationToken ct)
    {
        var role = await ApiRules.RoleAsync(workspaceId, http, access, ct);
        var project = await projects.VisibleProjects(workspaceId, ApiRules.UserId(http), role)
            .SingleOrDefaultAsync(x => x.Id == projectId, ct);
        ApiRules.Require(project != null, 404, "Resource not found.");
        return (project!, role);
    }
    public static void Write(Project project, WorkspaceRole role) =>
        ApiRules.Require(ProjectPermissions.CanWriteResources(role, true, project.ArchivedAt != null),
            project.ArchivedAt != null ? 409 : 403, project.ArchivedAt != null
                ? "Archived projects are read-only." : "You cannot change this project's work resources.");
    public static async Task<WorkItem> ItemAsync(FlowForgeDbContext db, Guid projectId, Guid itemId, CancellationToken ct)
    {
        var item = await db.WorkItems.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == itemId, ct);
        ApiRules.Require(item != null, 404, "Resource not found.");
        return item!;
    }
    public static void ItemWritable(WorkItem item) => ApiRules.Require(item.ArchivedAt == null, 409, "Archived work items are read-only.");
    public static async Task<WorkspaceMembership?> MemberAsync(FlowForgeDbContext db, Guid workspaceId,
        Guid projectId, Guid? userId, CancellationToken ct)
    {
        return await Infrastructure.WorkItems.WorkResourceRules.MemberAsync(db, workspaceId, projectId, userId, ct);
    }
    public static Task<ProjectStatus> StatusAsync(FlowForgeDbContext db, Guid projectId, string? name, CancellationToken ct) =>
        Infrastructure.WorkItems.WorkResourceRules.StatusAsync(db, projectId, name, ct);
    public static async Task CheckParentAsync(FlowForgeDbContext db, Guid projectId, Guid itemId, Guid? parentId, CancellationToken ct)
    {
        var seen = new HashSet<Guid> { itemId };
        var current = parentId;
        while (current != null)
        {
            ApiRules.Require(seen.Add(current.Value), 400, "Parent cycle is not allowed.");
            var parent = await db.WorkItems.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == current, ct);
            ApiRules.Require(parent != null && parent.ArchivedAt == null, 400, "Parent must be an active item in the same project.");
            current = parent!.ParentId;
        }
    }
    public static async Task<List<Guid>> LabelsAsync(FlowForgeDbContext db, Guid projectId, IReadOnlyList<Guid>? labels, CancellationToken ct)
    {
        return await Infrastructure.WorkItems.WorkResourceRules.LabelsAsync(db, projectId, labels, ct);
    }
    public static void Activity(FlowForgeDbContext db, Guid workspaceId, Guid? projectId, Guid actor,
        string action, string entityType, Guid entityId, object? before = null, object? after = null,
        Guid? workItemId = null) =>
        db.ActivityRecords.Add(new ActivityRecord { WorkspaceId = workspaceId, ProjectId = projectId,
            WorkItemId = workItemId ?? (entityType == "WorkItem" ? entityId : null),
            ActorUserId = actor, Action = action, EntityType = entityType, EntityId = entityId,
            BeforeValues = before == null ? null : JsonSerializer.Serialize(before),
            AfterValues = after == null ? null : JsonSerializer.Serialize(after),
            CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString() });
    public static void EmitEvent(FlowForgeDbContext db, Guid workspaceId, Guid? projectId, Guid actor,
        string eventType, Guid entityId, object payload)
    {
        var domainEvent = new DomainEvent { WorkspaceId = workspaceId, ProjectId = projectId,
            EventType = eventType, EntityId = entityId, ActorUserId = actor,
            Payload = JsonSerializer.Serialize(payload),
            CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N") };
        db.DomainEvents.Add(domainEvent);
        db.OutboxMessages.Add(new OutboxMessage { EventId = domainEvent.Id, MessageType = "DomainEvent" });
    }
    public static async Task<WorkItemDto> ItemDtoAsync(FlowForgeDbContext db, Project project, WorkItem item, CancellationToken ct)
    {
        var status = await db.ProjectStatuses.Where(x => x.Id == item.StatusId).Select(x => x.Name).SingleAsync(ct);
        var reporter = await db.WorkspaceMemberships.Where(x => x.Id == item.ReporterMembershipId).Select(x => x.UserId).SingleAsync(ct);
        var assignee = item.AssigneeMembershipId == null ? null : await db.WorkspaceMemberships
            .Where(x => x.Id == item.AssigneeMembershipId).Select(x => (Guid?)x.UserId).SingleAsync(ct);
        var labels = await db.WorkItemLabels.Where(x => x.WorkItemId == item.Id).Select(x => x.LabelId).ToListAsync(ct);
        var deps = await db.WorkItemDependencies.Where(x => x.WorkItemId == item.Id).Select(x => x.DependsOnItemId).ToListAsync(ct);
        var isBlocked = await db.WorkItemDependencies.AnyAsync(x => x.WorkItemId == item.Id &&
            db.WorkItems.Any(target => target.Id == x.DependsOnItemId &&
                db.ProjectStatuses.Any(s => s.Id == target.StatusId && s.Category != "Done")), ct);
        var needsReassignment = item.AssigneeMembershipId != null && await db.WorkspaceMemberships.AnyAsync(x =>
            x.Id == item.AssigneeMembershipId && (x.RemovedAt != null || !x.User.IsActive ||
                x.Role != WorkspaceRole.Owner && x.Role != WorkspaceRole.Admin &&
                !db.ProjectMemberships.Any(m => m.ProjectId == item.ProjectId && m.WorkspaceMembershipId == x.Id && m.RemovedAt == null)), ct);
        return new WorkItemDto(item.Id, item.ProjectId, $"{project.ProjectKey}-{item.ItemNumber}", item.ItemNumber,
            item.Title, item.Description, item.Type, status, item.Priority, reporter, assignee, item.ParentId,
            item.StartDate, item.DueDate, item.EstimateMinutes, labels, deps, isBlocked, needsReassignment, item.ArchivedAt,
            item.CreatedAt, item.UpdatedAt, item.Version);
    }
    public static Task<List<Guid>> MentionsAsync(FlowForgeDbContext db, Guid workspaceId, Guid projectId, string body, CancellationToken ct) =>
        Infrastructure.WorkItems.WorkResourceRules.MentionsAsync(db, workspaceId, projectId, body, ct);

    public static ActivityDto ActivityDto(ActivityRecord record) => new(record.Id, record.ProjectId, record.WorkItemId,
        record.ActorUserId, record.ActorKind, record.Action, record.EntityType, record.EntityId,
        record.BeforeValues == null ? null : JsonSerializer.Deserialize<JsonElement>(record.BeforeValues),
        record.AfterValues == null ? null : JsonSerializer.Deserialize<JsonElement>(record.AfterValues),
        record.CorrelationId, record.OccurredAt, record.WorkflowExecutionId);
}
