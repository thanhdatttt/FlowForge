using System.Text.Json;
using FlowForge.Application.Email;
using FlowForge.Application.Notifications;
using FlowForge.Domain.Notifications;
using FlowForge.Domain.WorkItems;
using FlowForge.Domain.Workflows;
using FlowForge.Infrastructure.Notifications;
using FlowForge.Infrastructure.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Infrastructure.Workflows;

public sealed class WorkflowActions(FlowForgeDbContext db, NotificationQueue notifications, IEmailSender sender,
    IEmailContentProtector protector, TimeProvider clock)
{
    public async Task ExecuteAsync(Workflow workflow, WorkflowExecution execution, DomainEvent source,
        WorkItem item, WorkflowAction action, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var before = new { item.StatusId, item.Priority, item.AssigneeMembershipId, item.Version };
        var oldStatus = item.StatusId;
        var oldAssignee = item.AssigneeMembershipId;
        var changed = false;
        Guid? commentId = null;
        object? detail = null;
        switch (action.Type)
        {
            case "Assign":
                var assignee = await WorkResourceRules.MemberAsync(db, item.WorkspaceId, item.ProjectId, action.UserId, ct);
                changed = item.AssigneeMembershipId != assignee!.Id; item.AssigneeMembershipId = assignee.Id;
                break;
            case "Unassign": changed = item.AssigneeMembershipId != null; item.AssigneeMembershipId = null; break;
            case "ChangeStatus":
                var status = await WorkResourceRules.StatusAsync(db, item.ProjectId, action.Value!.Trim(), ct);
                changed = item.StatusId != status.Id; item.StatusId = status.Id;
                break;
            case "ChangePriority": changed = item.Priority != action.Value; item.Priority = action.Value!; break;
            case "AddLabel":
            case "RemoveLabel":
                var label = await db.WorkLabels.SingleOrDefaultAsync(x => x.ProjectId == item.ProjectId && x.Name == action.Value!.Trim(), ct)
                    ?? throw new WorkResourceFailure("Label is not available in this project.");
                await WorkResourceRules.LabelsAsync(db, item.ProjectId, [label.Id], ct);
                var link = await db.WorkItemLabels.SingleOrDefaultAsync(x => x.WorkItemId == item.Id && x.LabelId == label.Id, ct);
                if (action.Type == "AddLabel" && link == null)
                { db.WorkItemLabels.Add(new WorkItemLabel { WorkItemId = item.Id, ProjectId = item.ProjectId, LabelId = label.Id }); changed = true; }
                if (action.Type == "RemoveLabel" && link != null) { db.WorkItemLabels.Remove(link); changed = true; }
                detail = new { LabelId = label.Id, action.Type };
                break;
            case "AddComment":
                var body = action.Value!.Trim();
                var mentions = await WorkResourceRules.MentionsAsync(db, item.WorkspaceId, item.ProjectId, body, ct);
                var comment = new WorkComment { WorkItemId = item.Id, AuthorUserId = workflow.CreatedByUserId,
                    Body = body, CreatedAt = now, UpdatedAt = now };
                db.WorkComments.Add(comment); commentId = comment.Id;
                foreach (var user in mentions) db.CommentMentions.Add(new CommentMention { CommentId = comment.Id, MentionedUserId = user });
                WorkflowEvents.Emit(db, execution, source, workflow.CreatedByUserId, "CommentAdded", comment.Id,
                    new { WorkItemId = item.Id, MentionedUserIds = mentions }, now);
                break;
            case "Notify":
                await WorkResourceRules.MemberAsync(db, item.WorkspaceId, item.ProjectId, action.UserId, ct);
                var notification = await notifications.AddAsync(new Notification { WorkspaceId = item.WorkspaceId,
                    ProjectId = item.ProjectId, WorkItemId = item.Id, RecipientUserId = action.UserId!.Value,
                    Type = "Workflow", IdempotencyKey = $"workflow:{execution.Id:N}:{action.Id:N}" }, ct, includeEmail: false);
                // The ordinary outbox publisher delivers the committed notification after this transaction.
                WorkflowEvents.Emit(db, execution, source, workflow.CreatedByUserId, "WorkflowNotificationCreated", notification.Id,
                    new { NotificationId = notification.Id, RecipientUserId = action.UserId.Value }, now);
                break;
            case "SendEmail":
                if (!sender.IsConfigured) throw new WorkResourceFailure("Email delivery is not configured.");
                await WorkResourceRules.MemberAsync(db, item.WorkspaceId, item.ProjectId, action.UserId, ct);
                var email = await db.Users.Where(x => x.Id == action.UserId && x.IsActive).Select(x => x.Email).SingleAsync(ct);
                db.EmailDeliveries.Add(new EmailDelivery { WorkspaceId = item.WorkspaceId, WorkflowExecutionId = execution.Id,
                    RecipientUserId = action.UserId, RecipientEmail = email, Subject = "FlowForge workflow notification",
                    ProtectedBody = protector.Protect(NotificationQueue.NotificationEmailBody),
                    IdempotencyKey = $"workflow:{execution.Id:N}:{action.Id:N}", CreatedAt = now, NextAttemptAt = now });
                break;
            default: throw new WorkResourceFailure("Unsupported workflow action.");
        }
        if (changed)
        {
            WorkResourceRules.Touch(item, now);
            WorkflowEvents.Emit(db, execution, source, workflow.CreatedByUserId, "WorkItemUpdated", item.Id,
                new { item.Id, item.Version, item.StatusId, item.AssigneeMembershipId, item.DueDate }, now);
            if (oldStatus != item.StatusId)
                WorkflowEvents.Emit(db, execution, source, workflow.CreatedByUserId, "WorkItemStatusChanged", item.Id,
                    new { item.Id, OldStatusId = oldStatus, NewStatusId = item.StatusId }, now);
            if (oldAssignee != item.AssigneeMembershipId)
                WorkflowEvents.Emit(db, execution, source, workflow.CreatedByUserId, "WorkItemAssignmentChanged", item.Id,
                    new { item.Id, OldAssigneeMembershipId = oldAssignee, NewAssigneeMembershipId = item.AssigneeMembershipId }, now);
        }
        db.ActivityRecords.Add(new ActivityRecord { WorkspaceId = item.WorkspaceId, ProjectId = item.ProjectId,
            WorkItemId = item.Id, ActorUserId = workflow.CreatedByUserId, ActorKind = "System",
            WorkflowExecutionId = execution.Id, CorrelationId = source.CorrelationId, Action = "workflow.action." + action.Type,
            EntityType = commentId == null ? "WorkItem" : "Comment", EntityId = commentId ?? item.Id, OccurredAt = now,
            BeforeValues = JsonSerializer.Serialize(before),
            AfterValues = JsonSerializer.Serialize(new { item.StatusId, item.Priority, item.AssigneeMembershipId, item.Version, Detail = detail, action.Id }) });
    }
}
