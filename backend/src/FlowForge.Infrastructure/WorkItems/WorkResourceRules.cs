using System.Text.RegularExpressions;
using FlowForge.Domain.WorkItems;
using FlowForge.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Infrastructure.WorkItems;

public sealed class WorkResourceFailure(string message) : Exception(message);

// Shared by request handlers and automation; callers own authorization and the workspace transaction.
public static partial class WorkResourceRules
{
    public static async Task<WorkspaceMembership?> MemberAsync(FlowForgeDbContext db, Guid workspaceId,
        Guid projectId, Guid? userId, CancellationToken ct)
    {
        if (userId == null) return null;
        var member = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId &&
            x.UserId == userId && x.RemovedAt == null && x.User.IsActive, ct);
        if (member == null) throw new WorkResourceFailure("The user must be an active workspace member.");
        if (member.Role is not (WorkspaceRole.Owner or WorkspaceRole.Admin) &&
            !await db.ProjectMemberships.AnyAsync(x => x.ProjectId == projectId && x.WorkspaceMembershipId == member.Id && x.RemovedAt == null, ct))
            throw new WorkResourceFailure("The user must have project access.");
        return member;
    }
    public static async Task<ProjectStatus> StatusAsync(FlowForgeDbContext db, Guid projectId, string? name, CancellationToken ct) =>
        await db.ProjectStatuses.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Name == name && x.IsActive, ct)
            ?? throw new WorkResourceFailure("Invalid project status.");
    public static async Task<List<Guid>> LabelsAsync(FlowForgeDbContext db, Guid projectId, IReadOnlyList<Guid>? labels, CancellationToken ct)
    {
        var ids = labels?.Distinct().ToList() ?? [];
        if (await db.WorkLabels.CountAsync(x => x.ProjectId == projectId && ids.Contains(x.Id), ct) != ids.Count)
            throw new WorkResourceFailure("Labels must belong to this project.");
        return ids;
    }
    public static async Task<List<Guid>> MentionsAsync(FlowForgeDbContext db, Guid workspaceId, Guid projectId, string body, CancellationToken ct)
    {
        var ids = MentionIds(body);
        foreach (var id in ids) await MemberAsync(db, workspaceId, projectId, id, ct);
        return ids;
    }
    public static List<Guid> MentionIds(string body) =>
        MentionRegex().Matches(body).Select(x => Guid.TryParse(x.Groups[1].Value, out var id) ? id : Guid.Empty)
            .Where(x => x != Guid.Empty).Distinct().ToList();
    public static void Touch(WorkItem item, DateTimeOffset now) { item.Version++; item.UpdatedAt = now; }
    [GeneratedRegex(@"@\{([0-9a-fA-F-]{36})\}", RegexOptions.CultureInvariant)]
    private static partial Regex MentionRegex();
}
