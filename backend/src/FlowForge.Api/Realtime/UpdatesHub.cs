using System.IdentityModel.Tokens.Jwt;
using FlowForge.Api.Validations.Realtime;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Notifications;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api.Realtime;

[Authorize]
public sealed class UpdatesHub(FlowForgeDbContext db, WorkspaceAccessService workspaces, NotificationAccess access) : Hub
{
    private Guid UserId => Guid.TryParse(Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new HubException("Authentication required.");
    public static string UserGroup(Guid userId) => $"user:{userId:N}";
    public static string ProjectGroup(Guid projectId, Guid userId) => $"project:{projectId:N}:user:{userId:N}";
    public static string WorkspaceGroup(Guid workspaceId, Guid userId) => $"workspace:{workspaceId:N}:user:{userId:N}";

    public override async Task OnConnectedAsync()
    {
        if (!await db.Users.AnyAsync(x => x.Id == UserId && x.IsActive, Context.ConnectionAborted))
        {
            Context.Abort();
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(UserId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public async Task JoinProject(Guid workspaceId, Guid projectId)
    {
        ValidateSubscription(workspaceId, projectId);
        if (!await access.ProjectRecipients(workspaceId, projectId).ContainsAsync(UserId, Context.ConnectionAborted))
            throw new HubException("Resource not found.");
        await Groups.AddToGroupAsync(Context.ConnectionId, ProjectGroup(projectId, UserId), Context.ConnectionAborted);
    }
    public Task LeaveProject(Guid projectId)
    {
        ValidateSubscription(projectId: projectId);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, ProjectGroup(projectId, UserId), Context.ConnectionAborted);
    }

    public async Task JoinWorkspace(Guid workspaceId)
    {
        ValidateSubscription(workspaceId: workspaceId);
        if (await workspaces.GetRoleAsync(workspaceId, UserId, Context.ConnectionAborted) == null)
            throw new HubException("Resource not found.");
        await Groups.AddToGroupAsync(Context.ConnectionId, WorkspaceGroup(workspaceId, UserId), Context.ConnectionAborted);
    }
    public Task LeaveWorkspace(Guid workspaceId)
    {
        ValidateSubscription(workspaceId: workspaceId);
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, WorkspaceGroup(workspaceId, UserId), Context.ConnectionAborted);
    }

    private static void ValidateSubscription(Guid? workspaceId = null, Guid? projectId = null)
    {
        var errors = SubscriptionRequestValidator.Validate(workspaceId, projectId);
        if (errors.Count > 0)
            throw new HubException(string.Join(" ", errors.Select(x => $"{x.Key}: {string.Join(" ", x.Value)}")));
    }
}
