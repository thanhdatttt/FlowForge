using FlowForge.Domain.Workflows;
using FlowForge.Infrastructure.Notifications;
using FlowForge.Infrastructure.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Infrastructure.Workflows;

public sealed class WorkflowReferences(FlowForgeDbContext db, NotificationAccess access)
{
    public async Task<Dictionary<string, string[]>> ValidateAsync(Guid workspaceId, Guid? projectId,
        WorkflowDefinition definition, CancellationToken ct)
    {
        var errors = WorkflowDefinitionRules.Validate(definition);
        if (errors.Count != 0) return errors;
        for (var i = 0; i < definition.Actions!.Count; i++)
        {
            var action = definition.Actions[i];
            var valid = true;
            if (action.UserId is Guid userId)
                valid = await RecipientAvailableAsync(workspaceId, projectId, userId, ct);
            if (action.Type == "AddComment")
                foreach (var mention in WorkResourceRules.MentionIds(action.Value!))
                    if (!await RecipientAvailableAsync(workspaceId, projectId, mention, ct)) { valid = false; break; }
            if (action.Type == "ChangeStatus")
                valid = await db.ProjectStatuses.AnyAsync(x => x.Name == action.Value!.Trim() && x.IsActive &&
                    db.Projects.Any(p => p.Id == x.ProjectId && p.WorkspaceId == workspaceId && p.ArchivedAt == null &&
                        (projectId == null || p.Id == projectId)), ct);
            if (action.Type is "AddLabel" or "RemoveLabel")
                valid = await db.WorkLabels.AnyAsync(x => x.Name == action.Value!.Trim() &&
                    db.Projects.Any(p => p.Id == x.ProjectId && p.WorkspaceId == workspaceId && p.ArchivedAt == null &&
                        (projectId == null || p.Id == projectId)), ct);
            if (!valid) errors[$"definition.actions[{i}]"] = ["The action target is not available in this scope."];
        }
        return errors;
    }
    private Task<bool> RecipientAvailableAsync(Guid workspaceId, Guid? projectId, Guid userId, CancellationToken ct) =>
        projectId is Guid project
            ? access.ProjectRecipients(workspaceId, project).ContainsAsync(userId, ct)
            : db.WorkspaceMemberships.AnyAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId &&
                x.RemovedAt == null && x.User.IsActive, ct);

}
