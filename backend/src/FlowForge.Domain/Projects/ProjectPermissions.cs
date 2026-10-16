using FlowForge.Domain.Workspaces;

namespace FlowForge.Domain.Projects;

public static class ProjectPermissions
{
    public static bool CanCreate(WorkspaceRole role) => role is WorkspaceRole.Owner or WorkspaceRole.Admin;
    public static bool CanRead(WorkspaceRole role, bool isMember) => CanCreate(role) || isMember;
    public static bool CanManage(WorkspaceRole role, bool isMember) => CanCreate(role) || role == WorkspaceRole.Manager && isMember;
    public static bool CanWriteResources(WorkspaceRole role, bool isMember, bool archived) =>
        !archived && CanRead(role, isMember) && role != WorkspaceRole.Viewer;
}
