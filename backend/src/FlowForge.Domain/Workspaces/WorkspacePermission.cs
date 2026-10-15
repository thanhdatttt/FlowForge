namespace FlowForge.Domain.Workspaces;

public enum WorkspacePermission
{
    Read,
    ManageWorkspace,
    ManageMembers,
    ManageProjects,
    WriteWorkItems,
    TransferOwnership
}

public static class WorkspacePermissions
{
    public static bool Allows(WorkspaceRole role, WorkspacePermission permission) => permission switch
    {
        WorkspacePermission.Read => true,
        WorkspacePermission.ManageWorkspace or WorkspacePermission.ManageMembers =>
            role is WorkspaceRole.Owner or WorkspaceRole.Admin,
        WorkspacePermission.ManageProjects =>
            role is WorkspaceRole.Owner or WorkspaceRole.Admin or WorkspaceRole.Manager,
        WorkspacePermission.WriteWorkItems => role is not WorkspaceRole.Viewer,
        WorkspacePermission.TransferOwnership => role is WorkspaceRole.Owner,
        _ => false
    };

    public static bool CanManageRole(WorkspaceRole actor, WorkspaceRole target) =>
        Allows(actor, WorkspacePermission.ManageMembers) &&
        target is not WorkspaceRole.Owner &&
        (actor == WorkspaceRole.Owner || target != WorkspaceRole.Admin);

    public static bool CanAssignRole(WorkspaceRole actor, WorkspaceRole role) =>
        Allows(actor, WorkspacePermission.ManageMembers) &&
        role is not WorkspaceRole.Owner &&
        (actor == WorkspaceRole.Owner || role != WorkspaceRole.Admin);
}
