using Microsoft.EntityFrameworkCore;
using FlowForge.Domain.Identity;

namespace FlowForge.Infrastructure;

public sealed class FlowForgeDbContext(DbContextOptions<FlowForgeDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

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
    }
}
