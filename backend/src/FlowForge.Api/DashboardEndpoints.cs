using System.Data;
using FlowForge.Api.Contracts.Dashboard;
using FlowForge.Api.Contracts.WorkItems;
using FlowForge.Api.Validations;
using FlowForge.Domain.WorkItems;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Projects;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}/projects/{projectId:guid}/dashboard")
            .WithTags("Dashboard").RequireAuthorization();

        group.MapGet("/", async (Guid workspaceId, Guid projectId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db,
            TimeProvider clock, CancellationToken ct) =>
        {
            // Counts, breakdowns and activity describe one database snapshot even during concurrent writes.
            await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            var asOf = clock.GetUtcNow();
            var today = DateOnly.FromDateTime(asOf.UtcDateTime);
            var items = ActiveItems(db, workspaceId, projectId);
            var counts = await items.GroupBy(x => new { x.StatusId, x.Priority })
                .Select(g => new { g.Key.StatusId, g.Key.Priority, Count = g.Count() }).ToListAsync(ct);
            var statuses = await db.ProjectStatuses.AsNoTracking().Where(x => x.ProjectId == projectId)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(ct);
            var byStatus = statuses.Where(s => s.IsActive || counts.Any(c => c.StatusId == s.Id))
                .Select(s => new StatusCountDto(s.Id, s.Name, s.Category,
                    counts.Where(c => c.StatusId == s.Id).Sum(c => c.Count))).ToList();
            var byPriority = new[] { "Low", "Medium", "High", "Critical" }
                .Select(p => new PriorityCountDto(p, counts.Where(c => c.Priority == p).Sum(c => c.Count))).ToList();
            var total = byStatus.Sum(x => x.Count);
            var completed = byStatus.Where(x => x.Category == "Done").Sum(x => x.Count);
            var overdue = await OverdueItems(db, workspaceId, projectId, today).CountAsync(ct);
            var activity = await db.ActivityRecords.AsNoTracking()
                .Where(x => x.WorkspaceId == workspaceId && x.ProjectId == projectId)
                .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Take(10).ToListAsync(ct);
            var result = new ProjectDashboardDto(projectId, asOf, today, total, completed,
                total == 0 ? 0 : decimal.Round(completed * 100m / total, 2, MidpointRounding.AwayFromZero),
                overdue, byStatus, byPriority, activity.Select(WorkItemSupport.ActivityDto).ToList());
            await snapshot.CommitAsync(ct);
            return Results.Ok(result);
        });

        group.MapGet("/workload", async (Guid workspaceId, Guid projectId, int? page, int? pageSize,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects,
            FlowForgeDbContext db, CancellationToken ct) =>
        {
            await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            var errors = PaginationValidator.Validate(page, pageSize);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            var (number, size) = PaginationValidator.Defaults(page, pageSize);
            // Preserve actual assignment, including retained memberships; reporting never silently reassigns work.
            var query = from item in OpenItems(db, workspaceId, projectId)
                join member in db.WorkspaceMemberships on item.AssigneeMembershipId equals (Guid?)member.Id into members
                from member in members.DefaultIfEmpty()
                group item by new
                {
                    UserId = member == null ? (Guid?)null : member.UserId,
                    DisplayName = member == null ? "Unassigned" : member.User.DisplayName
                } into workload
                select new
                {
                    AssigneeUserId = workload.Key.UserId, workload.Key.DisplayName,
                    OpenItems = workload.Count(), EstimateMinutes = workload.Sum(x => (long?)x.EstimateMinutes) ?? 0
                };
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(x => x.OpenItems).ThenBy(x => x.AssigneeUserId)
                .Skip((number - 1) * size).Take(size)
                .Select(x => new AssigneeWorkloadDto(x.AssigneeUserId, x.DisplayName, x.OpenItems, x.EstimateMinutes))
                .ToListAsync(ct);
            await snapshot.CommitAsync(ct);
            return Results.Ok(new PageDto<AssigneeWorkloadDto>(rows, total, number, size));
        });

        group.MapGet("/overdue", async (Guid workspaceId, Guid projectId, int? page, int? pageSize,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects,
            FlowForgeDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            var (project, _) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            var errors = PaginationValidator.Validate(page, pageSize);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            var (number, size) = PaginationValidator.Defaults(page, pageSize);
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var query = OverdueItems(db, workspaceId, projectId, today);
            var total = await query.CountAsync(ct);
            var rows = await (from item in query
                join status in db.ProjectStatuses on item.StatusId equals status.Id
                orderby item.DueDate, item.ItemNumber
                select new OverdueWorkItemDto(item.Id, project.ProjectKey + "-" + item.ItemNumber,
                    item.Title, item.Type, status.Name, item.Priority,
                    db.WorkspaceMemberships.Where(m => m.Id == item.AssigneeMembershipId)
                        .Select(m => (Guid?)m.UserId).SingleOrDefault(), item.DueDate!.Value, item.EstimateMinutes))
                .Skip((number - 1) * size).Take(size).ToListAsync(ct);
            await snapshot.CommitAsync(ct);
            return Results.Ok(new PageDto<OverdueWorkItemDto>(rows, total, number, size));
        });
        return app;
    }

    private static IQueryable<WorkItem> ActiveItems(FlowForgeDbContext db, Guid workspaceId, Guid projectId) =>
        db.WorkItems.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.ProjectId == projectId && x.ArchivedAt == null);

    private static IQueryable<WorkItem> OpenItems(FlowForgeDbContext db, Guid workspaceId, Guid projectId) =>
        ActiveItems(db, workspaceId, projectId)
            .Where(x => db.ProjectStatuses.Any(s => s.Id == x.StatusId && s.ProjectId == projectId && s.Category != "Done"));

    private static IQueryable<WorkItem> OverdueItems(FlowForgeDbContext db, Guid workspaceId, Guid projectId, DateOnly today) =>
        OpenItems(db, workspaceId, projectId).Where(x => x.DueDate < today);
}
