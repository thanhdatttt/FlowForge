using Microsoft.EntityFrameworkCore;

namespace FlowForge.Infrastructure.Workspaces;

public static class WorkspaceLock
{
    // Every workspace-scoped mutation takes this lock before checking permissions.
    // A lock on the root also serializes invitation acceptance and ownership transfer.
    public static Task<Domain.Workspaces.Workspace?> AcquireAsync(
        FlowForgeDbContext db, Guid workspaceId, CancellationToken cancellationToken) =>
        db.Workspaces.FromSqlInterpolated($"SELECT * FROM workspaces WHERE id = {workspaceId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
}
