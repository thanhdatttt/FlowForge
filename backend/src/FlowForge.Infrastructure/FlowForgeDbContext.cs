using Microsoft.EntityFrameworkCore;
using FlowForge.Domain.Identity;
using FlowForge.Domain.Workspaces;
using FlowForge.Domain.Projects;

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
    }
}
