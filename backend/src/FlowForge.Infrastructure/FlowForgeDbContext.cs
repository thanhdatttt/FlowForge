using Microsoft.EntityFrameworkCore;
using FlowForge.Domain.Identity;
using FlowForge.Domain.Workspaces;
using FlowForge.Domain.Projects;
using FlowForge.Domain.WorkItems;
using FlowForge.Domain.Notifications;

namespace FlowForge.Infrastructure;

public sealed class FlowForgeDbContext(DbContextOptions<FlowForgeDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();

    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<WorkspaceInvitation> WorkspaceInvitations => Set<WorkspaceInvitation>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMembership> ProjectMemberships => Set<ProjectMembership>();
    public DbSet<ProjectStatus> ProjectStatuses => Set<ProjectStatus>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<WorkLabel> WorkLabels => Set<WorkLabel>();
    public DbSet<WorkItemLabel> WorkItemLabels => Set<WorkItemLabel>();
    public DbSet<WorkItemDependency> WorkItemDependencies => Set<WorkItemDependency>();
    public DbSet<WorkComment> WorkComments => Set<WorkComment>();
    public DbSet<CommentMention> CommentMentions => Set<CommentMention>();
    public DbSet<WorkAttachment> WorkAttachments => Set<WorkAttachment>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<ActivityRecord> ActivityRecords => Set<ActivityRecord>();
    public DbSet<DomainEvent> DomainEvents => Set<DomainEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<EmailDelivery> EmailDeliveries => Set<EmailDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Id).HasColumnName("id");
            entity.Property(user => user.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
            entity.Property(user => user.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(320).IsRequired();
            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
            entity.Property(user => user.PasswordHash).HasColumnName("password_hash").IsRequired();
            entity.Property(user => user.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
            entity.Property(user => user.EmailVerifiedAt).HasColumnName("email_verified_at");
            entity.Property(user => user.IsActive).HasColumnName("is_active");
            entity.Property(user => user.CreatedAt).HasColumnName("created_at");
            entity.Property(user => user.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<RefreshSession>(entity =>
        {
            entity.ToTable("refresh_sessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Id).HasColumnName("id");
            entity.Property(session => session.UserId).HasColumnName("user_id");
            entity.Property(session => session.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            entity.HasIndex(session => session.TokenHash).IsUnique();
            entity.Property(session => session.FamilyId).HasColumnName("family_id");
            entity.HasIndex(session => session.FamilyId);
            entity.Property(session => session.ExpiresAt).HasColumnName("expires_at");
            entity.Property(session => session.RevokedAt).HasColumnName("revoked_at");
            entity.Property(session => session.ReplacedById).HasColumnName("replaced_by_id");
            entity.Property(session => session.CreatedAt).HasColumnName("created_at");
            entity.HasOne(session => session.User).WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RefreshSession>().WithMany().HasForeignKey(session => session.ReplacedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces");
            entity.HasKey(workspace => workspace.Id);
            entity.Property(workspace => workspace.Id).HasColumnName("id");
            entity.Property(workspace => workspace.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            entity.Property(workspace => workspace.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(workspace => workspace.CreatedAt).HasColumnName("created_at");
            entity.Property(workspace => workspace.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne<User>().WithMany().HasForeignKey(workspace => workspace.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkspaceMembership>(entity =>
        {
            entity.ToTable("workspace_memberships", table => table.HasCheckConstraint(
                "CK_workspace_memberships_role", "role IN ('Owner', 'Admin', 'Manager', 'Developer', 'Viewer')"));
            entity.HasKey(membership => membership.Id);
            entity.HasAlternateKey(membership => new { membership.WorkspaceId, membership.Id });
            entity.Property(membership => membership.Id).HasColumnName("id");
            entity.Property(membership => membership.WorkspaceId).HasColumnName("workspace_id");
            entity.Property(membership => membership.UserId).HasColumnName("user_id");
            entity.Property(membership => membership.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20);
            entity.Property(membership => membership.JoinedAt).HasColumnName("joined_at");
            entity.Property(membership => membership.RemovedAt).HasColumnName("removed_at");
            entity.HasOne(membership => membership.Workspace).WithMany().HasForeignKey(membership => membership.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(membership => membership.User).WithMany().HasForeignKey(membership => membership.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(membership => new { membership.WorkspaceId, membership.UserId })
                .IsUnique().HasFilter("removed_at IS NULL");
            entity.HasIndex(membership => membership.WorkspaceId)
                .IsUnique().HasFilter("removed_at IS NULL AND role = 'Owner'");
        });

        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.ToTable("email_verification_tokens", table => table.HasCheckConstraint(
                "CK_email_verification_failed_attempts", "failed_attempts BETWEEN 0 AND 5"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(320);
            entity.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.Property(x => x.UsedAt).HasColumnName("used_at");
            entity.Property(x => x.FailedAttempts).HasColumnName("failed_attempts");
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WorkspaceInvitation>(entity =>
        {
            entity.ToTable("workspace_invitations", table =>
            {
                table.HasCheckConstraint("CK_invitation_role", "role IN ('Admin', 'Manager', 'Developer', 'Viewer')");
                table.HasCheckConstraint("CK_invitation_terminal", "accepted_at IS NULL OR revoked_at IS NULL");
                table.HasCheckConstraint("CK_invitation_expiry", "expires_at > created_at");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            entity.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
            entity.Property(x => x.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(320);
            entity.Property(x => x.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64);
            entity.Property(x => x.InvitedByUserId).HasColumnName("invited_by_user_id");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            entity.Property(x => x.AcceptedAt).HasColumnName("accepted_at");
            entity.Property(x => x.RevokedAt).HasColumnName("revoked_at");
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.WorkspaceId, x.NormalizedEmail }).IsUnique()
                .HasFilter("accepted_at IS NULL AND revoked_at IS NULL");
            entity.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("projects", table =>
            {
                table.HasCheckConstraint("CK_project_key", "project_key ~ '^[A-Z][A-Z0-9]{1,9}$'");
                table.HasCheckConstraint("CK_project_dates", "starts_on IS NULL OR target_on IS NULL OR starts_on <= target_on");
            });
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            entity.Property(x => x.ProjectKey).HasColumnName("project_key").HasMaxLength(10);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(100);
            entity.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000);
            entity.Property(x => x.LeadMembershipId).HasColumnName("lead_membership_id");
            entity.Property(x => x.StartsOn).HasColumnName("starts_on");
            entity.Property(x => x.TargetOn).HasColumnName("target_on");
            entity.Property(x => x.ArchivedAt).HasColumnName("archived_at");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(x => x.NextItemNumber).HasColumnName("next_item_number");
            entity.HasIndex(x => new { x.WorkspaceId, x.ProjectKey }).IsUnique();
            entity.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.LeadMembership).WithMany()
                .HasForeignKey(x => new { x.WorkspaceId, x.LeadMembershipId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ProjectMembership>(entity =>
        {
            entity.ToTable("project_memberships");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            entity.Property(x => x.ProjectId).HasColumnName("project_id");
            entity.Property(x => x.WorkspaceMembershipId).HasColumnName("workspace_membership_id");
            entity.Property(x => x.JoinedAt).HasColumnName("joined_at");
            entity.Property(x => x.RemovedAt).HasColumnName("removed_at");
            entity.HasIndex(x => new { x.ProjectId, x.WorkspaceMembershipId }).IsUnique().HasFilter("removed_at IS NULL");
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.WorkspaceMembership).WithMany().HasForeignKey(x => new { x.WorkspaceId, x.WorkspaceMembershipId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ProjectStatus>(e =>
        {
            e.ToTable("project_statuses", t => t.HasCheckConstraint("CK_status_category", "category IN ('Open', 'Done')"));
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.ProjectId, x.Id });
            e.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(40);
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(20);
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WorkItem>(e =>
        {
            e.ToTable("work_items", t =>
            {
                t.HasCheckConstraint("CK_work_item_type", "type IN ('Task', 'Issue')");
                t.HasCheckConstraint("CK_work_item_priority", "priority IN ('Low', 'Medium', 'High', 'Critical')");
                t.HasCheckConstraint("CK_work_item_estimate", "estimate_minutes IS NULL OR estimate_minutes >= 0");
                t.HasCheckConstraint("CK_work_item_dates", "start_date IS NULL OR due_date IS NULL OR start_date <= due_date");
                t.HasCheckConstraint("CK_work_item_number", "item_number > 0");
                t.HasCheckConstraint("CK_work_item_parent", "parent_id IS NULL OR parent_id <> id");
            });
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.ProjectId, x.Id });
            e.HasIndex(x => new { x.ProjectId, x.ItemNumber }).IsUnique();
            e.HasIndex(x => new { x.ProjectId, x.StatusId });
            e.HasIndex(x => new { x.ProjectId, x.AssigneeMembershipId });
            e.HasIndex(x => new { x.ProjectId, x.DueDate });
            e.HasIndex(x => new { x.ProjectId, x.UpdatedAt });
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.ItemNumber).HasColumnName("item_number");
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(20000);
            e.Property(x => x.Type).HasColumnName("type").HasMaxLength(12);
            e.Property(x => x.StatusId).HasColumnName("status_id");
            e.Property(x => x.Priority).HasColumnName("priority").HasMaxLength(12);
            e.Property(x => x.ReporterMembershipId).HasColumnName("reporter_membership_id");
            e.Property(x => x.AssigneeMembershipId).HasColumnName("assignee_membership_id");
            e.Property(x => x.ParentId).HasColumnName("parent_id");
            e.Property(x => x.StartDate).HasColumnName("start_date");
            e.Property(x => x.DueDate).HasColumnName("due_date");
            e.Property(x => x.EstimateMinutes).HasColumnName("estimate_minutes");
            e.Property(x => x.ArchivedAt).HasColumnName("archived_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProjectStatus>().WithMany().HasForeignKey(x => new { x.ProjectId, x.StatusId })
                .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkspaceMembership>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ReporterMembershipId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkspaceMembership>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AssigneeMembershipId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkItem>().WithMany().HasForeignKey(x => new { x.ProjectId, x.ParentId })
                .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WorkLabel>(e =>
        {
            e.ToTable("labels"); e.HasKey(x => x.Id); e.HasAlternateKey(x => new { x.ProjectId, x.Id });
            e.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(50);
            e.Property(x => x.Color).HasColumnName("color").HasMaxLength(7);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WorkItemLabel>(e =>
        {
            e.ToTable("work_item_labels"); e.HasKey(x => new { x.WorkItemId, x.LabelId });
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.WorkItemId).HasColumnName("work_item_id");
            e.Property(x => x.LabelId).HasColumnName("label_id");
            e.HasOne<WorkItem>().WithMany().HasForeignKey(x => new { x.ProjectId, x.WorkItemId })
                .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkLabel>().WithMany().HasForeignKey(x => new { x.ProjectId, x.LabelId })
                .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WorkItemDependency>(e =>
        {
            e.ToTable("work_item_dependencies", t => t.HasCheckConstraint("CK_dependency_self", "work_item_id <> depends_on_item_id"));
            e.HasKey(x => new { x.WorkItemId, x.DependsOnItemId });
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.WorkItemId).HasColumnName("work_item_id");
            e.Property(x => x.DependsOnItemId).HasColumnName("depends_on_item_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.DependsOnItemId);
            e.HasOne<WorkItem>().WithMany().HasForeignKey(x => new { x.ProjectId, x.WorkItemId })
                .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkItem>().WithMany().HasForeignKey(x => new { x.ProjectId, x.DependsOnItemId })
                .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WorkComment>(e =>
        {
            e.ToTable("comments"); e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.WorkItemId, x.Id });
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.WorkItemId).HasColumnName("work_item_id");
            e.Property(x => x.AuthorUserId).HasColumnName("author_user_id");
            e.Property(x => x.Body).HasColumnName("body").HasMaxLength(10000);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.HasOne<WorkItem>().WithMany().HasForeignKey(x => x.WorkItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<CommentMention>(e =>
        {
            e.ToTable("comment_mentions"); e.HasKey(x => new { x.CommentId, x.MentionedUserId });
            e.Property(x => x.CommentId).HasColumnName("comment_id");
            e.Property(x => x.MentionedUserId).HasColumnName("mentioned_user_id");
            e.HasOne<WorkComment>().WithMany().HasForeignKey(x => x.CommentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.MentionedUserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<WorkAttachment>(e =>
        {
            e.ToTable("attachments", t => t.HasCheckConstraint("CK_attachment_size", "size_bytes > 0"));
            e.HasKey(x => x.Id); e.HasIndex(x => x.StorageKey).IsUnique();
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.WorkItemId).HasColumnName("work_item_id");
            e.Property(x => x.UploadedByUserId).HasColumnName("uploaded_by_user_id");
            e.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(100);
            e.Property(x => x.OriginalName).HasColumnName("original_name").HasMaxLength(255);
            e.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100);
            e.Property(x => x.SizeBytes).HasColumnName("size_bytes");
            e.Property(x => x.CreatedAt).HasColumnName("created_at"); e.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            e.HasOne<WorkItem>().WithMany().HasForeignKey(x => x.WorkItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<TimeEntry>(e =>
        {
            e.ToTable("time_entries", t => t.HasCheckConstraint("CK_time_minutes", "minutes > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.WorkItemId).HasColumnName("work_item_id");
            e.Property(x => x.UserId).HasColumnName("user_id"); e.Property(x => x.Minutes).HasColumnName("minutes");
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.Note).HasColumnName("note").HasMaxLength(2000);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.DeletedAt).HasColumnName("deleted_at");
            e.HasOne<WorkItem>().WithMany().HasForeignKey(x => x.WorkItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ActivityRecord>(e =>
        {
            e.ToTable("activity_records"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.WorkspaceId, x.OccurredAt });
            e.HasIndex(x => new { x.ProjectId, x.OccurredAt });
            e.HasIndex(x => new { x.WorkItemId, x.OccurredAt });
            e.Property(x => x.Id).HasColumnName("id"); e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.WorkItemId).HasColumnName("work_item_id");
            e.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
            e.Property(x => x.ActorKind).HasColumnName("actor_kind").HasMaxLength(20);
            e.Property(x => x.Action).HasColumnName("action").HasMaxLength(80);
            e.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(40);
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.BeforeValues).HasColumnName("before_values").HasColumnType("jsonb");
            e.Property(x => x.AfterValues).HasColumnName("after_values").HasColumnType("jsonb");
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64);
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkItem>().WithMany().HasForeignKey(x => new { x.ProjectId, x.WorkItemId })
                .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<DomainEvent>(e =>
        {
            e.ToTable("domain_events", t => t.HasCheckConstraint("CK_domain_event_depth", "depth >= 0"));
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.OccurredAt });
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(80);
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
            e.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64);
            e.Property(x => x.OriginExecutionId).HasColumnName("origin_execution_id");
            e.Property(x => x.Depth).HasColumnName("depth");
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages", t => t.HasCheckConstraint("CK_outbox_attempt_count", "attempt_count >= 0"));
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EventId).IsUnique();
            e.HasIndex(x => new { x.ProcessedAt, x.AvailableAt });
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.EventId).HasColumnName("event_id");
            e.Property(x => x.MessageType).HasColumnName("message_type").HasMaxLength(80);
            e.Property(x => x.AvailableAt).HasColumnName("available_at");
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            e.Property(x => x.AttemptCount).HasColumnName("attempt_count");
            e.Property(x => x.LockId).HasColumnName("lock_id");
            e.Property(x => x.FailedAt).HasColumnName("failed_at");
            e.Property(x => x.SafeError).HasColumnName("safe_error").HasMaxLength(100);
            e.HasOne<DomainEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Notification>(e =>
        {
            e.ToTable("notifications", t =>
            {
                t.HasCheckConstraint("CK_notification_type", "type IN ('Assigned', 'Mentioned', 'Overdue', 'Invited')");
                t.HasCheckConstraint("CK_notification_scope", "(project_id IS NULL OR work_item_id IS NOT NULL) AND (work_item_id IS NULL OR project_id IS NOT NULL) AND ((type = 'Invited' AND invitation_id IS NOT NULL AND project_id IS NULL) OR (type <> 'Invited' AND invitation_id IS NULL AND work_item_id IS NOT NULL))");
            });
            e.HasKey(x => x.Id);
            e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RecipientUserId).HasColumnName("recipient_user_id");
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.ProjectId).HasColumnName("project_id");
            e.Property(x => x.WorkItemId).HasColumnName("work_item_id");
            e.Property(x => x.CommentId).HasColumnName("comment_id");
            e.Property(x => x.InvitationId).HasColumnName("invitation_id");
            e.Property(x => x.DueDate).HasColumnName("due_date");
            e.Property(x => x.Type).HasColumnName("type").HasMaxLength(30);
            e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.ReadAt).HasColumnName("read_at");
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => new { x.RecipientUserId, x.ReadAt, x.CreatedAt });
            e.HasIndex(x => new { x.WorkItemId, x.DueDate, x.RecipientUserId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Project>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.ProjectId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkItem>().WithMany().HasForeignKey(x => new { x.ProjectId, x.WorkItemId })
                .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkComment>().WithMany().HasForeignKey(x => new { x.WorkItemId, x.CommentId })
                .HasPrincipalKey(x => new { x.WorkItemId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkspaceInvitation>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.InvitationId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<EmailDelivery>(e =>
        {
            e.ToTable("email_deliveries", t =>
            {
                t.HasCheckConstraint("CK_email_delivery_status", "status IN ('Pending', 'Sent', 'Cancelled', 'Failed')");
                t.HasCheckConstraint("CK_email_delivery_attempts", "attempt_count >= 0");
                t.HasCheckConstraint("CK_email_delivery_target", "(notification_id IS NULL) <> (invitation_id IS NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            e.Property(x => x.NotificationId).HasColumnName("notification_id");
            e.Property(x => x.InvitationId).HasColumnName("invitation_id");
            e.Property(x => x.RecipientEmail).HasColumnName("recipient_email").HasMaxLength(320);
            e.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(200);
            e.Property(x => x.ProtectedBody).HasColumnName("protected_body");
            e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
            e.Property(x => x.AttemptCount).HasColumnName("attempt_count");
            e.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
            e.Property(x => x.LockId).HasColumnName("lock_id");
            e.Property(x => x.SafeError).HasColumnName("safe_error").HasMaxLength(100);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Notification>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.NotificationId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WorkspaceInvitation>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.InvitationId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
