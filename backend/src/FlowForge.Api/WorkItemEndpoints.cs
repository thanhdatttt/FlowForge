using FlowForge.Api.Contracts.WorkItems;
using FlowForge.Api.Validations;
using FlowForge.Api.Validations.WorkItems;
using FlowForge.Domain.WorkItems;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Projects;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

public static class WorkItemEndpoints
{
    public static IEndpointRouteBuilder MapWorkItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}/projects/{projectId:guid}")
            .WithTags("Work Items").RequireAuthorization().AddEndpointFilter<WorkspaceMutationFilter>();
        group.MapGet("/statuses", async (Guid workspaceId, Guid projectId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            var statuses = await db.ProjectStatuses.Where(x => x.ProjectId == projectId && x.IsActive)
                .OrderBy(x => x.SortOrder).Select(x => new StatusDto(x.Id, x.Name, x.Category, x.SortOrder)).ToListAsync(ct);
            return Results.Ok(statuses);
        });
        group.MapPost("/labels", async (Guid workspaceId, Guid projectId, LabelWriteDto request, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            var errors = LabelWriteRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var label = new WorkLabel { ProjectId = projectId, Name = request.Name!.Trim(), Color = request.Color };
            db.WorkLabels.Add(label);
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "label.created", "Label", label.Id,
                after: new { label.Name, label.Color });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/workspaces/{workspaceId}/projects/{projectId}/labels/{label.Id}",
                new LabelDto(label.Id, label.Name, label.Color));
        });
        group.MapGet("/labels", async (Guid workspaceId, Guid projectId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            return Results.Ok(await db.WorkLabels.AsNoTracking().Where(x => x.ProjectId == projectId)
                .OrderBy(x => x.Name).Select(x => new LabelDto(x.Id, x.Name, x.Color)).ToListAsync(ct));
        });
        group.MapPut("/labels/{labelId:guid}", async (Guid workspaceId, Guid projectId, Guid labelId, LabelWriteDto request,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            var label = await db.WorkLabels.SingleOrDefaultAsync(x => x.Id == labelId && x.ProjectId == projectId, ct);
            ApiRules.Require(label != null, 404, "Resource not found.");
            var errors = LabelWriteRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var before = new { label!.Name, label.Color };
            label.Name = request.Name!.Trim(); label.Color = request.Color;
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "label.updated", "Label", label.Id,
                before, new { label.Name, label.Color });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new LabelDto(label.Id, label.Name, label.Color));
        });
        group.MapDelete("/labels/{labelId:guid}", async (Guid workspaceId, Guid projectId, Guid labelId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            var label = await db.WorkLabels.SingleOrDefaultAsync(x => x.Id == labelId && x.ProjectId == projectId, ct);
            ApiRules.Require(label != null, 404, "Resource not found.");
            var links = await db.WorkItemLabels.Where(x => x.LabelId == labelId).ToListAsync(ct);
            var affectedIds = links.Select(x => x.WorkItemId).Distinct().ToArray();
            var affectedItems = await db.WorkItems.Where(x => affectedIds.Contains(x.Id)).ToListAsync(ct);
            foreach (var item in affectedItems)
            {
                Touch(item);
                WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http),
                    "work-item.label-removed", "WorkItem", item.Id,
                    before: new { LabelId = labelId });
                WorkItemSupport.EmitEvent(db, workspaceId, projectId, ApiRules.UserId(http), "WorkItemUpdated", item.Id,
                    new { item.Id, item.Version, RemovedLabelId = labelId });
            }
            db.WorkItemLabels.RemoveRange(links);
            db.WorkLabels.Remove(label!);
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "label.deleted", "Label", labelId,
                before: new { label!.Name, label.Color });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        group.MapPost("/work-items", async (Guid workspaceId, Guid projectId, WorkItemWriteDto request, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            var errors = WorkItemWriteRequestValidator.Validate(request, false);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var actor = await WorkItemSupport.MemberAsync(db, workspaceId, projectId, ApiRules.UserId(http), ct);
            var assignee = await WorkItemSupport.MemberAsync(db, workspaceId, projectId, request.AssigneeUserId, ct);
            await WorkItemSupport.CheckParentAsync(db, projectId, Guid.Empty, request.ParentId, ct);
            var labels = await WorkItemSupport.LabelsAsync(db, projectId, request.LabelIds, ct);
            var status = await WorkItemSupport.StatusAsync(db, projectId, request.Status, ct);
            var item = new WorkItem { WorkspaceId = workspaceId, ProjectId = projectId,
                ItemNumber = project.NextItemNumber++, Title = request.Title!.Trim(), Description = request.Description?.Trim(),
                Type = request.Type!, StatusId = status.Id, Priority = request.Priority!,
                ReporterMembershipId = actor!.Id, AssigneeMembershipId = assignee?.Id, ParentId = request.ParentId,
                StartDate = request.StartDate, DueDate = request.DueDate, EstimateMinutes = request.EstimateMinutes };
            db.WorkItems.Add(item);
            foreach (var id in labels) db.WorkItemLabels.Add(new WorkItemLabel { ProjectId = projectId, WorkItemId = item.Id, LabelId = id });
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "work-item.created", "WorkItem", item.Id,
                after: new { item.ItemNumber, item.Title, item.Description, item.Type, Status = request.Status,
                    item.Priority, request.AssigneeUserId, item.ParentId, item.StartDate, item.DueDate,
                    item.EstimateMinutes, LabelIds = labels });
            WorkItemSupport.EmitEvent(db, workspaceId, projectId, ApiRules.UserId(http), "WorkItemCreated", item.Id,
                new { item.Id, item.ItemNumber, item.StatusId, item.AssigneeMembershipId, item.DueDate });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{item.Id}",
                await WorkItemSupport.ItemDtoAsync(db, project, item, ct));
        });
        group.MapGet("/work-items", async (Guid workspaceId, Guid projectId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db,
            string? search, string? status, string? priority, string? type, Guid? assigneeUserId,
            Guid? reporterUserId, Guid? labelId, DateOnly? dueBefore, DateOnly? dueAfter, bool? archived, string? sort,
            bool? descending, int? page, int? pageSize, CancellationToken ct) =>
        {
            var (project, _) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            var errors = WorkItemListQueryValidator.Validate(search, status, priority, type,
                dueBefore, dueAfter, sort, page, pageSize);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var (number, size) = PaginationValidator.Defaults(page, pageSize);
            var query = db.WorkItems.AsNoTracking().Where(x => x.ProjectId == projectId);
            if (archived != null) query = query.Where(x => (x.ArchivedAt != null) == archived);
            else query = query.Where(x => x.ArchivedAt == null);
            if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToLower(); query = query.Where(x => x.Title.ToLower().Contains(term) || x.ItemNumber.ToString() == term); }
            if (status != null) query = query.Where(x => db.ProjectStatuses.Any(s => s.Id == x.StatusId && s.Name == status));
            if (priority != null) query = query.Where(x => x.Priority == priority);
            if (type != null) query = query.Where(x => x.Type == type);
            if (assigneeUserId != null) query = query.Where(x => db.WorkspaceMemberships.Any(m => m.Id == x.AssigneeMembershipId && m.UserId == assigneeUserId));
            if (reporterUserId != null) query = query.Where(x => db.WorkspaceMemberships.Any(m => m.Id == x.ReporterMembershipId && m.UserId == reporterUserId));
            if (labelId != null) query = query.Where(x => db.WorkItemLabels.Any(l => l.WorkItemId == x.Id && l.LabelId == labelId));
            if (dueBefore != null) query = query.Where(x => x.DueDate != null && x.DueDate <= dueBefore);
            if (dueAfter != null) query = query.Where(x => x.DueDate != null && x.DueDate >= dueAfter);
            var total = await query.CountAsync(ct);
            var desc = descending ?? true;
            query = (sort ?? "updatedAt") switch
            {
                "createdAt" => desc ? query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id) : query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
                "dueDate" => desc ? query.OrderByDescending(x => x.DueDate).ThenBy(x => x.Id) : query.OrderBy(x => x.DueDate).ThenBy(x => x.Id),
                "priority" => desc ? query.OrderByDescending(x => x.Priority == "Critical" ? 4 :
                    x.Priority == "High" ? 3 : x.Priority == "Medium" ? 2 : 1).ThenBy(x => x.Id) :
                    query.OrderBy(x => x.Priority == "Critical" ? 4 : x.Priority == "High" ? 3 :
                        x.Priority == "Medium" ? 2 : 1).ThenBy(x => x.Id),
                "key" => desc ? query.OrderByDescending(x => x.ItemNumber).ThenBy(x => x.Id) : query.OrderBy(x => x.ItemNumber).ThenBy(x => x.Id),
                _ => desc ? query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id) : query.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id)
            };
            var rows = await query.Skip((number - 1) * size).Take(size).ToListAsync(ct);
            var items = new List<WorkItemDto>();
            foreach (var row in rows) items.Add(await WorkItemSupport.ItemDtoAsync(db, project, row, ct));
            return Results.Ok(new PageDto<WorkItemDto>(items, total, number, size));
        });
        group.MapGet("/work-items/{itemId:guid}", async (Guid workspaceId, Guid projectId, Guid itemId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, _) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            var item = await WorkItemSupport.ItemAsync(db, projectId, itemId, ct);
            return Results.Ok(await WorkItemSupport.ItemDtoAsync(db, project, item, ct));
        });
        group.MapPut("/work-items/{itemId:guid}", async (Guid workspaceId, Guid projectId, Guid itemId, WorkItemWriteDto request,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            var item = await WorkItemSupport.ItemAsync(db, projectId, itemId, ct);
            WorkItemSupport.ItemWritable(item);
            var errors = WorkItemWriteRequestValidator.Validate(request, true);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            ApiRules.Require(request.Version == item.Version, 409, "The work item changed. Reload and retry.");
            await WorkItemSupport.CheckParentAsync(db, projectId, itemId, request.ParentId, ct);
            var assignee = await WorkItemSupport.MemberAsync(db, workspaceId, projectId, request.AssigneeUserId, ct);
            var labels = await WorkItemSupport.LabelsAsync(db, projectId, request.LabelIds, ct);
            var status = await WorkItemSupport.StatusAsync(db, projectId, request.Status, ct);
            var before = new { item.Title, item.Description, item.Type, item.StatusId, item.Priority,
                item.AssigneeMembershipId, item.ParentId, item.StartDate, item.DueDate, item.EstimateMinutes };
            var oldStatusId = item.StatusId;
            var oldAssigneeId = item.AssigneeMembershipId;
            item.Title = request.Title!.Trim(); item.Description = request.Description?.Trim(); item.Type = request.Type!;
            item.StatusId = status.Id; item.Priority = request.Priority!; item.AssigneeMembershipId = assignee?.Id;
            item.ParentId = request.ParentId; item.StartDate = request.StartDate; item.DueDate = request.DueDate;
            item.EstimateMinutes = request.EstimateMinutes; Touch(item);
            var oldLabels = await db.WorkItemLabels.Where(x => x.WorkItemId == itemId).ToListAsync(ct);
            var oldLabelIds = oldLabels.Select(x => x.LabelId).ToArray();
            db.WorkItemLabels.RemoveRange(oldLabels.Where(x => !labels.Contains(x.LabelId)));
            foreach (var id in labels.Except(oldLabels.Select(x => x.LabelId)))
                db.WorkItemLabels.Add(new WorkItemLabel { ProjectId = projectId, WorkItemId = itemId, LabelId = id });
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "work-item.updated", "WorkItem", itemId,
                new { before.Title, before.Description, before.Type, before.StatusId, before.Priority,
                    before.AssigneeMembershipId, before.ParentId, before.StartDate, before.DueDate,
                    before.EstimateMinutes, LabelIds = oldLabelIds },
                new { item.Title, item.Description, item.Type, item.StatusId, item.Priority,
                    item.AssigneeMembershipId, item.ParentId, item.StartDate, item.DueDate, item.EstimateMinutes,
                    LabelIds = labels });
            WorkItemSupport.EmitEvent(db, workspaceId, projectId, ApiRules.UserId(http), "WorkItemUpdated", itemId,
                new { item.Id, item.Version, item.StatusId, item.AssigneeMembershipId, item.DueDate });
            if (oldStatusId != item.StatusId)
                WorkItemSupport.EmitEvent(db, workspaceId, projectId, ApiRules.UserId(http), "WorkItemStatusChanged", itemId,
                    new { item.Id, OldStatusId = oldStatusId, NewStatusId = item.StatusId });
            if (oldAssigneeId != item.AssigneeMembershipId)
                WorkItemSupport.EmitEvent(db, workspaceId, projectId, ApiRules.UserId(http), "WorkItemAssignmentChanged", itemId,
                    new { item.Id, OldAssigneeMembershipId = oldAssigneeId, NewAssigneeMembershipId = item.AssigneeMembershipId });
            await db.SaveChangesAsync(ct);
            return Results.Ok(await WorkItemSupport.ItemDtoAsync(db, project, item, ct));
        });
        group.MapPost("/work-items/{itemId:guid}/archive", (Guid workspaceId, Guid projectId, Guid itemId,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
            ArchiveAsync(workspaceId, projectId, itemId, http, access, projects, db, true, ct));
        group.MapPost("/work-items/{itemId:guid}/restore", (Guid workspaceId, Guid projectId, Guid itemId,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
            ArchiveAsync(workspaceId, projectId, itemId, http, access, projects, db, false, ct));
        group.MapPost("/work-items/{itemId:guid}/dependencies", async (Guid workspaceId, Guid projectId, Guid itemId,
            DependencyWriteDto request, HttpContext http, WorkspaceAccessService access, ProjectAccessService projects,
            FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            var item = await WorkItemSupport.ItemAsync(db, projectId, itemId, ct);
            WorkItemSupport.ItemWritable(item);
            var errors = DependencyWriteRequestValidator.Validate(request, itemId);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var target = await WorkItemSupport.ItemAsync(db, projectId, request.DependsOnItemId, ct);
            WorkItemSupport.ItemWritable(target);
            ApiRules.Require(!await db.WorkItemDependencies.AnyAsync(x => x.WorkItemId == itemId && x.DependsOnItemId == target.Id, ct),
                409, "Dependency already exists.");
            var visited = new HashSet<Guid>();
            var queue = new Queue<Guid>(); queue.Enqueue(target.Id);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                ApiRules.Require(current != itemId, 400, "Dependency cycle is not allowed.");
                if (!visited.Add(current)) continue;
                foreach (var next in await db.WorkItemDependencies.Where(x => x.WorkItemId == current)
                    .Select(x => x.DependsOnItemId).ToListAsync(ct)) queue.Enqueue(next);
            }
            db.WorkItemDependencies.Add(new WorkItemDependency { ProjectId = projectId, WorkItemId = itemId,
                DependsOnItemId = target.Id });
            Touch(item);
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "dependency.added", "WorkItem", itemId,
                after: new { DependsOnItemId = target.Id });
            WorkItemSupport.EmitEvent(db, workspaceId, projectId, ApiRules.UserId(http), "WorkItemUpdated", itemId,
                new { item.Id, item.Version, AddedDependencyId = target.Id });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/dependencies/{target.Id}",
                new { dependsOnItemId = target.Id });
        });
        group.MapDelete("/work-items/{itemId:guid}/dependencies/{dependsOnId:guid}", async (Guid workspaceId, Guid projectId,
            Guid itemId, Guid dependsOnId, HttpContext http, WorkspaceAccessService access, ProjectAccessService projects,
            FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            var item = await WorkItemSupport.ItemAsync(db, projectId, itemId, ct);
            WorkItemSupport.ItemWritable(item);
            var link = await db.WorkItemDependencies.SingleOrDefaultAsync(x => x.ProjectId == projectId &&
                x.WorkItemId == itemId && x.DependsOnItemId == dependsOnId, ct);
            ApiRules.Require(link != null, 404, "Resource not found.");
            db.WorkItemDependencies.Remove(link!);
            Touch(item);
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "dependency.removed", "WorkItem", itemId,
                before: new { DependsOnItemId = dependsOnId });
            WorkItemSupport.EmitEvent(db, workspaceId, projectId, ApiRules.UserId(http), "WorkItemUpdated", itemId,
                new { item.Id, item.Version, RemovedDependencyId = dependsOnId });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        return app;
    }
    private static async Task<IResult> ArchiveAsync(Guid workspaceId, Guid projectId, Guid itemId, HttpContext http,
        WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, bool archive, CancellationToken ct)
    {
        var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
        WorkItemSupport.Write(project, role);
        var item = await WorkItemSupport.ItemAsync(db, projectId, itemId, ct);
        if ((item.ArchivedAt != null) != archive)
        {
            item.ArchivedAt = archive ? DateTimeOffset.UtcNow : null; Touch(item);
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http),
                archive ? "work-item.archived" : "work-item.restored", "WorkItem", itemId);
            WorkItemSupport.EmitEvent(db, workspaceId, projectId, ApiRules.UserId(http),
                archive ? "WorkItemArchived" : "WorkItemRestored", itemId, new { item.Id, item.Version });
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(await WorkItemSupport.ItemDtoAsync(db, project, item, ct));
    }
    private static void Touch(WorkItem item) => Infrastructure.WorkItems.WorkResourceRules.Touch(item, DateTimeOffset.UtcNow);
}
