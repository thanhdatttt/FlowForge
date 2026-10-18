using FlowForge.Domain.Identity;
using FlowForge.Domain.Projects;
using FlowForge.Domain.WorkItems;
using FlowForge.Domain.Workspaces;
using FlowForge.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Infrastructure.Workflows;

internal static class WorkflowModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<Workflow>(e =>
        {
            e.ToTable("workflows", t =>
            {
                t.HasCheckConstraint("CK_workflow_version", "current_version > 0");
                t.HasCheckConstraint("CK_workflow_enabled", "NOT is_enabled OR enabled_at IS NOT NULL");
            });
            e.HasKey(x => x.Id); e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100);
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
            e.Property(x => x.IsEnabled).HasColumnName("is_enabled");
            e.Property(x => x.EnabledAt).HasColumnName("enabled_at");
            e.Property(x => x.CurrentVersion).HasColumnName("current_version").IsConcurrencyToken();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.WorkspaceId, x.ProjectId, x.IsEnabled });
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<WorkflowVersion>(e =>
        {
            e.ToTable("workflow_versions", t =>
            {
                t.HasCheckConstraint("CK_workflow_trigger", "trigger_type IN ('WorkItemCreated', 'WorkItemUpdated', 'WorkItemStatusChanged', 'WorkItemAssignmentChanged', 'CommentAdded', 'WorkItemDueDateReached', 'WorkItemDueDateOverdue')");
                t.HasCheckConstraint("CK_workflow_version_number", "version_number > 0");
            });
            e.HasKey(x => x.Id); e.HasAlternateKey(x => new { x.WorkflowId, x.Id });
            e.HasIndex(x => new { x.WorkflowId, x.VersionNumber }).IsUnique();
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WorkflowId).HasColumnName("workflow_id");
            e.Property(x => x.VersionNumber).HasColumnName("version_number");
            e.Property(x => x.TriggerType).HasColumnName("trigger_type").HasMaxLength(80);
            e.Property(x => x.Configuration).HasColumnName("configuration").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne<Workflow>().WithMany().HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<WorkflowExecution>(e =>
        {
            e.ToTable("workflow_executions", t =>
            {
                t.HasCheckConstraint("CK_workflow_execution_status", "status IN ('Pending', 'Running', 'Succeeded', 'Failed', 'Cancelled')");
                t.HasCheckConstraint("CK_workflow_execution_attempts", "attempt_count >= 0");
            });
            e.HasKey(x => x.Id); e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
            e.HasIndex(x => new { x.WorkflowId, x.EventId }).IsUnique();
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => new { x.WorkflowId, x.CreatedAt });
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.WorkItemId).HasColumnName("work_item_id");
            e.Property(x => x.WorkflowId).HasColumnName("workflow_id");
            e.Property(x => x.WorkflowVersionId).HasColumnName("workflow_version_id");
            e.Property(x => x.EventId).HasColumnName("event_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
            e.Property(x => x.ConditionsMatched).HasColumnName("conditions_matched");
            e.Property(x => x.AttemptCount).HasColumnName("attempt_count");
            e.Property(x => x.LockId).HasColumnName("lock_id");
            e.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.FinishedAt).HasColumnName("finished_at");
            e.Property(x => x.SafeError).HasColumnName("safe_error").HasMaxLength(100);
            e.HasOne<Workflow>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.WorkflowId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkflowVersion>().WithMany().HasForeignKey(x => new { x.WorkflowId, x.WorkflowVersionId })
                .HasPrincipalKey(x => new { x.WorkflowId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DomainEvent>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.EventId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkItem>().WithMany().HasForeignKey(x => new { x.ProjectId, x.WorkItemId })
                .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<WorkflowActionResult>(e =>
        {
            e.ToTable("workflow_action_results", t =>
            {
                t.HasCheckConstraint("CK_workflow_action_status", "status IN ('Pending', 'Succeeded', 'Failed', 'Cancelled')");
                t.HasCheckConstraint("CK_workflow_action_attempts", "attempt_count >= 0");
            });
            e.HasKey(x => new { x.ExecutionId, x.ActionId });
            e.Property(x => x.ExecutionId).HasColumnName("execution_id");
            e.Property(x => x.ActionId).HasColumnName("action_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
            e.Property(x => x.AttemptCount).HasColumnName("attempt_count");
            e.Property(x => x.SafeError).HasColumnName("safe_error").HasMaxLength(100);
            e.Property(x => x.FinishedAt).HasColumnName("finished_at");
            e.HasOne<WorkflowExecution>().WithMany().HasForeignKey(x => x.ExecutionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
