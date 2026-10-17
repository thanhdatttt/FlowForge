using FlowForge.Domain.Projects;
using FlowForge.Api.Validation.Identity;
using FlowForge.Domain.Workspaces;
using FlowForge.Infrastructure.Identity;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseTwoRulesTests
{
    [Theory]
    [InlineData(WorkspaceRole.Owner, true, true, true)]
    [InlineData(WorkspaceRole.Admin, true, true, true)]
    [InlineData(WorkspaceRole.Manager, false, true, true)]
    [InlineData(WorkspaceRole.Developer, false, false, true)]
    [InlineData(WorkspaceRole.Viewer, false, false, false)]
    public void Project_permissions_distinguish_metadata_management_and_resource_writes(
        WorkspaceRole role, bool create, bool manage, bool write)
    {
        Assert.Equal(create, ProjectPermissions.CanCreate(role));
        Assert.Equal(manage, ProjectPermissions.CanManage(role, true));
        Assert.Equal(write, ProjectPermissions.CanWriteResources(role, true, false));
        Assert.False(ProjectPermissions.CanWriteResources(role, true, true));
        Assert.Equal(create, ProjectPermissions.CanRead(role, false));
        Assert.Equal(create, ProjectPermissions.CanManage(role, false));
    }
    [Fact]
    public void Tokens_have_random_entropy_and_stable_nonplaintext_hashes()
    {
        var first = SecureTokens.Generate();
        var second = SecureTokens.Generate();
        Assert.Equal(64, first.Length);
        Assert.NotEqual(first, second);
        Assert.NotEqual(first, SecureTokens.Hash(first));
        Assert.Equal(SecureTokens.Hash(first), SecureTokens.Hash(first));
    }
    [Fact]
    public void Otp_codes_are_six_digits_and_hashes_are_bound_to_the_user_challenge_and_secret()
    {
        Assert.Matches("^[0-9]{6}$", EmailVerificationCodes.Generate());
        Assert.Empty(EmailVerificationRequestValidator.Validate(new("000123")));
        Assert.Contains("code", EmailVerificationRequestValidator.Validate(new("12345")).Keys);
        Assert.Contains("code", EmailVerificationRequestValidator.Validate(new("１２３４５６")).Keys);
        var id = Guid.NewGuid();
        var user = Guid.NewGuid();
        const string key = "unit-test-otp-secret-at-least-32-bytes";
        var hash = EmailVerificationCodes.Hash("000123", id, user, key);
        Assert.True(EmailVerificationCodes.Matches("000123", id, user, key, hash));
        Assert.False(EmailVerificationCodes.Matches("000124", id, user, key, hash));
        Assert.False(EmailVerificationCodes.Matches("000123", Guid.NewGuid(), user, key, hash));
        Assert.False(EmailVerificationCodes.Matches("000123", id, Guid.NewGuid(), key, hash));
        Assert.False(EmailVerificationCodes.Matches("000123", id, user, key + "different", hash));
    }

    [Fact]
    public void Invitation_state_preserves_terminal_outcomes_and_expired_history()
    {
        var now = DateTimeOffset.UtcNow;
        var invitation = new WorkspaceInvitation { NormalizedEmail = "USER@EXAMPLE.COM", TokenHash = "hash", ExpiresAt = now.AddDays(7) };
        Assert.Equal("Pending", invitation.Status(now));
        invitation.RevokedAt = now;
        Assert.Equal("Revoked", invitation.Status(now.AddDays(8)));
        invitation.RevokedAt = null;
        Assert.Equal("Expired", invitation.Status(now.AddDays(8)));
        invitation.RevokedAt = now.AddDays(8);
        Assert.Equal("Expired", invitation.Status(now.AddDays(8)));
        invitation.RevokedAt = null;
        invitation.AcceptedAt = now;
        Assert.Equal("Accepted", invitation.Status(now.AddDays(8)));
    }
}
