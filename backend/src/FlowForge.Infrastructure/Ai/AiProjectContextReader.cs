using FlowForge.Application.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlowForge.Infrastructure.Ai;

// The caller owns a repeatable-read transaction containing authorization and context reads.
public sealed class AiProjectContextReader(FlowForgeDbContext db, TimeProvider clock, IOptions<AiOptions> options)
{
    public async Task<AiProjectSummaryContext> ReadAsync(Guid workspaceId, Guid projectId, string projectName, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var query = from item in db.WorkItems.AsNoTracking()
            join status in db.ProjectStatuses on item.StatusId equals status.Id
            where item.WorkspaceId == workspaceId && item.ProjectId == projectId && item.ArchivedAt == null
            select new
            {
                Item = item, Status = status.Name, Completed = status.Category == "Done",
                Blocked = status.Category != "Done" && (status.Name == "Blocked" || db.WorkItemDependencies.Any(d =>
                    d.WorkItemId == item.Id && db.WorkItems.Any(target => target.Id == d.DependsOnItemId &&
                        db.ProjectStatuses.Any(s => s.Id == target.StatusId && s.Category != "Done"))))
            };
        var aggregate = await query.GroupBy(x => 1).Select(g => new
        {
            Total = g.Count(), Completed = g.Count(x => x.Completed),
            Overdue = g.Count(x => !x.Completed && x.Item.DueDate < today), Blocked = g.Count(x => x.Blocked)
        }).SingleOrDefaultAsync(ct);
        var total = aggregate?.Total ?? 0;
        var completed = aggregate?.Completed ?? 0;
        var facts = new AiProjectFacts(total, completed,
            total == 0 ? 0 : decimal.Round(completed * 100m / total, 2, MidpointRounding.AwayFromZero),
            aggregate?.Overdue ?? 0, aggregate?.Blocked ?? 0);
        var key = await db.Projects.Where(x => x.WorkspaceId == workspaceId && x.Id == projectId).Select(x => x.ProjectKey).SingleAsync(ct);
        var items = await query.OrderByDescending(x => x.Item.UpdatedAt).ThenBy(x => x.Item.Id)
            .Take(options.Value.MaxContextItems).Select(x => new AiSummaryWorkItem(x.Item.Id, key + "-" + x.Item.ItemNumber,
                x.Item.Title, x.Item.Description == null ? null : x.Item.Description.Substring(0, Math.Min(x.Item.Description.Length, 2000)),
                x.Item.Type, x.Status, x.Completed, x.Item.Priority, x.Item.DueDate, x.Blocked)).ToListAsync(ct);
        return new(workspaceId, projectId, projectName, now, today, facts, items, total > items.Count);
    }
}
