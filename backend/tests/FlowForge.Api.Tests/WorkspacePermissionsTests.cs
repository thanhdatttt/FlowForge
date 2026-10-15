using FlowForge.Domain.Workspaces;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class WorkspacePermissionsTests
{
    [Theory]
    [InlineData(WorkspaceRole.Owner, true, true, true, true)]
    [InlineData(WorkspaceRole.Admin, true, true, true, false)]
    [InlineData(WorkspaceRole.Manager, false, true, true, false)]
    [InlineData(WorkspaceRole.Developer, false, false, true, false)]
    [InlineData(WorkspaceRole.Viewer, false, false, false, false)]
    public void Roles_grant_only_their_intended_permissions(
        WorkspaceRole role, bool manageMembers, bool manageProjects, bool writeWorkItems, bool transferOwnership)
    {
        Assert.True(WorkspacePermissions.Allows(role, WorkspacePermission.Read));
        Assert.Equal(manageMembers, WorkspacePermissions.Allows(role, WorkspacePermission.ManageMembers));
        Assert.Equal(manageMembers, WorkspacePermissions.Allows(role, WorkspacePermission.ManageWorkspace));
        Assert.Equal(manageProjects, WorkspacePermissions.Allows(role, WorkspacePermission.ManageProjects));
        Assert.Equal(writeWorkItems, WorkspacePermissions.Allows(role, WorkspacePermission.WriteWorkItems));
        Assert.Equal(transferOwnership, WorkspacePermissions.Allows(role, WorkspacePermission.TransferOwnership));
    }

    [Fact]
    public void Admin_cannot_assign_or_modify_owner_or_admin_roles()
    {
        Assert.False(WorkspacePermissions.CanAssignRole(WorkspaceRole.Admin, WorkspaceRole.Owner));
        Assert.False(WorkspacePermissions.CanAssignRole(WorkspaceRole.Admin, WorkspaceRole.Admin));
        Assert.False(WorkspacePermissions.CanManageRole(WorkspaceRole.Admin, WorkspaceRole.Owner));
        Assert.False(WorkspacePermissions.CanManageRole(WorkspaceRole.Admin, WorkspaceRole.Admin));
        Assert.True(WorkspacePermissions.CanAssignRole(WorkspaceRole.Admin, WorkspaceRole.Developer));
        Assert.True(WorkspacePermissions.CanManageRole(WorkspaceRole.Admin, WorkspaceRole.Viewer));
    }
}
