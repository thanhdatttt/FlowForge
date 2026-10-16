using System.Net;
using System.Net.Http.Json;
using FlowForge.Api.Contracts.Identity;
using FlowForge.Api.Contracts.Projects;
using FlowForge.Api.Contracts.Workspaces;
using FlowForge.Domain.Identity;
using FlowForge.Domain.Projects;
using FlowForge.Domain.Workspaces;
using FlowForge.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseTwoIntegrationTests(PhaseTwoFixture fixture) : IClassFixture<PhaseTwoFixture>
{
    [PostgresFact]
    public async Task Verification_requires_authentication_and_matching_unexpired_single_use_code()
    {
        var user = await fixture.UserAsync(false);
        var other = await fixture.UserAsync(false);
        using var client = fixture.Client(user);
        using var anonymous = fixture.Client();
        using var outsider = fixture.Client(other);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/v1/auth/email-verification/request", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/email-verification/request", null)).StatusCode);
        var raw = fixture.Sender.Codes[user.Email];
        Assert.Equal(HttpStatusCode.BadRequest, (await outsider.PostAsJsonAsync("/api/v1/auth/email-verification/confirm", new { code = raw })).StatusCode);
        await fixture.InDb(async db =>
        {
            var stored = await db.EmailVerificationTokens.SingleAsync(x => x.UserId == user.Id);
            Assert.NotEqual(raw, stored.TokenHash);
            Assert.Matches("^[0-9]{6}$", raw);
            Assert.Equal(EmailVerificationCodes.Hash(raw, stored.Id, user.Id, PhaseTwoFixture.SigningKey), stored.TokenHash);
        });
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            client.PostAsJsonAsync("/api/v1/auth/email-verification/confirm", new { code = raw })));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.BadRequest);
        var me = await client.GetFromJsonAsync<CurrentUserResponseDto>("/api/v1/auth/me");
        Assert.True(me!.EmailVerified);
    }

    [PostgresFact]
    public async Task Expired_verification_and_delivery_failures_never_verify_the_user()
    {
        var user = await fixture.UserAsync(false);
        using var client = fixture.Client(user);
        var raw = "000123";
        await fixture.InDb(async db =>
        {
            var challenge = new EmailVerificationToken { UserId = user.Id, NormalizedEmail = user.NormalizedEmail,
                TokenHash = "", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2), ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) };
            challenge.TokenHash = EmailVerificationCodes.Hash(raw, challenge.Id, user.Id, PhaseTwoFixture.SigningKey);
            db.EmailVerificationTokens.Add(challenge);
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/email-verification/confirm", new { code = raw })).StatusCode);
        fixture.Sender.IsConfigured = false;
        try { Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync("/api/v1/auth/email-verification/request", null)).StatusCode); }
        finally { fixture.Sender.IsConfigured = true; }
        fixture.Sender.FailDelivery = true;
        try { Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync("/api/v1/auth/email-verification/request", null)).StatusCode); }
        finally { fixture.Sender.FailDelivery = false; }
        await fixture.InDb(async db =>
        {
            Assert.Null((await db.Users.SingleAsync(x => x.Id == user.Id)).EmailVerifiedAt);
            Assert.Equal(1, await db.EmailVerificationTokens.CountAsync(x => x.UserId == user.Id));
        });
    }

    [PostgresFact]
    public async Task Invitation_can_precede_registration_and_acceptance_is_atomic()
    {
        var owner = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner);
        using var client = fixture.Client(owner);
        var email = Guid.NewGuid().ToString("N") + "@example.com";
        var invitation = await InviteAsync(client, workspace.Id, email);
        Assert.EndsWith($"#token={invitation.Token}", invitation.InvitationUrl);
        Assert.Contains($"/api/v1/invitations/{invitation.Invitation.Id}", invitation.InvitationUrl);
        using var anonymous = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/invitations/accept", new { token = invitation.Token })).StatusCode);
        var registered = await anonymous.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequestDto(email, "StrongPassword1!", "Invited User"));
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var tokens = await registered.Content.ReadFromJsonAsync<AuthTokenResponseDto>();
        anonymous.DefaultRequestHeaders.Authorization = new("Bearer", tokens!.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.PostAsJsonAsync("/api/v1/invitations/accept", new { token = invitation.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsync("/api/v1/auth/email-verification/request", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/v1/auth/email-verification/confirm", new { code = fixture.Sender.Codes[email] })).StatusCode);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => anonymous.PostAsJsonAsync("/api/v1/invitations/accept", new { token = invitation.Token })));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await fixture.InDb(async db =>
        {
            var stored = await db.WorkspaceInvitations.SingleAsync(x => x.Id == invitation.Invitation.Id);
            Assert.NotEqual(invitation.Token, stored.TokenHash);
            Assert.NotNull(stored.AcceptedAt);
            Assert.Equal(1, await db.WorkspaceMemberships.CountAsync(x => x.WorkspaceId == workspace.Id && x.User.NormalizedEmail == email.ToUpperInvariant() && x.RemovedAt == null));
        });
        var listing = await client.GetStringAsync($"/api/v1/workspaces/{workspace.Id}/invitations");
        Assert.DoesNotContain(invitation.Token, listing);
        Assert.DoesNotContain("tokenHash", listing);
    }

    [PostgresFact]
    public async Task Invitation_roles_identity_expiration_revocation_and_rejection_are_enforced()
    {
        var owner = await fixture.UserAsync();
        var admin = await fixture.UserAsync();
        var recipient = await fixture.UserAsync();
        var outsider = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (admin, WorkspaceRole.Admin));
        using var client = fixture.Client(owner);
        using var adminClient = fixture.Client(admin);
        using var recipientClient = fixture.Client(recipient);
        using var outsiderClient = fixture.Client(outsider);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/invitations", new { email = recipient.Email, role = "Admin" })).StatusCode);
        var invitation = await InviteAsync(client, workspace.Id, recipient.Email);
        var previewPath = $"/api/v1/invitations/{invitation.Invitation.Id}";
        Assert.Equal(HttpStatusCode.OK, (await recipientClient.GetAsync(previewPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync(previewPath)).StatusCode);
        Assert.DoesNotContain(invitation.Token, await recipientClient.GetStringAsync(previewPath));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/invitations", new { email = recipient.Email, role = "Developer" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.PostAsJsonAsync("/api/v1/invitations/accept", new { token = invitation.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync($"/api/v1/workspaces/{workspace.Id}/invitations")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/workspaces/{workspace.Id}/invitations/{invitation.Invitation.Id}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await recipientClient.PostAsJsonAsync("/api/v1/invitations/accept", new { token = invitation.Token })).StatusCode);
        invitation = await InviteAsync(client, workspace.Id, recipient.Email);
        Assert.Equal(HttpStatusCode.NoContent, (await recipientClient.PostAsJsonAsync("/api/v1/invitations/reject", new { token = invitation.Token })).StatusCode);
        invitation = await InviteAsync(client, workspace.Id, recipient.Email);
        await fixture.InDb(async db =>
        {
            var stored = await db.WorkspaceInvitations.SingleAsync(x => x.Id == invitation.Invitation.Id);
            stored.CreatedAt = DateTimeOffset.UtcNow.AddDays(-8);
            stored.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Conflict, (await recipientClient.PostAsJsonAsync("/api/v1/invitations/accept", new { token = invitation.Token })).StatusCode);
        await InviteAsync(client, workspace.Id, recipient.Email);
        await fixture.InDb(async db => Assert.Equal("Expired", (await db.WorkspaceInvitations.SingleAsync(x => x.Id == invitation.Invitation.Id)).Status(DateTimeOffset.UtcNow)));
    }

    [PostgresFact]
    public async Task Invitations_do_not_retain_a_removed_inviters_authority()
    {
        var owner = await fixture.UserAsync();
        var admin = await fixture.UserAsync();
        var recipient = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (admin, WorkspaceRole.Admin));
        using var client = fixture.Client(owner);
        using var adminClient = fixture.Client(admin);
        using var recipientClient = fixture.Client(recipient);
        var invitation = await InviteAsync(adminClient, workspace.Id, recipient.Email);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/workspaces/{workspace.Id}/members/{admin.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await recipientClient.PostAsJsonAsync("/api/v1/invitations/accept", new { token = invitation.Token })).StatusCode);
    }

    [PostgresFact]
    public async Task Ownership_transfer_requires_a_verified_active_member_and_serializes_concurrent_transfers()
    {
        var owner = await fixture.UserAsync();
        var first = await fixture.UserAsync();
        var second = await fixture.UserAsync();
        var unverified = await fixture.UserAsync(false);
        var outsider = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (first, WorkspaceRole.Developer),
            (second, WorkspaceRole.Manager), (unverified, WorkspaceRole.Developer));
        using var client = fixture.Client(owner);
        using var memberClient = fixture.Client(first);
        var route = $"/api/v1/workspaces/{workspace.Id}/ownership/transfer";
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PostAsJsonAsync(route, new { newOwnerUserId = second.Id })).StatusCode);
        foreach (var invalid in new[] { unverified.Id, outsider.Id, owner.Id })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route, new { newOwnerUserId = invalid })).StatusCode);
        var responses = await Task.WhenAll(new[] { first, second }.Select(x => client.PostAsJsonAsync(route, new { newOwnerUserId = x.Id })));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Forbidden);
        Guid winner = default;
        await fixture.InDb(async db =>
        {
            var members = await db.WorkspaceMemberships.Where(x => x.WorkspaceId == workspace.Id && x.RemovedAt == null).ToListAsync();
            winner = Assert.Single(members, x => x.Role == WorkspaceRole.Owner).UserId;
            Assert.Equal(WorkspaceRole.Admin, members.Single(x => x.UserId == owner.Id).Role);
        });
        using var newOwner = fixture.Client(winner == first.Id ? first : second);
        Assert.Equal(HttpStatusCode.OK, (await newOwner.PutAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/members/{owner.Id}/role", new { role = "Viewer" })).StatusCode);
        await fixture.InDb(async db =>
        {
            var member = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == workspace.Id && x.UserId == winner && x.RemovedAt == null);
            member.Role = WorkspaceRole.Admin;
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)exception.InnerException!).SqlState);
        });
    }

    [PostgresFact]
    public async Task Projects_enforce_explicit_membership_role_rules_and_workspace_isolation()
    {
        var owner = await fixture.UserAsync();
        var admin = await fixture.UserAsync();
        var manager = await fixture.UserAsync();
        var developer = await fixture.UserAsync();
        var viewer = await fixture.UserAsync();
        var outsider = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (admin, WorkspaceRole.Admin), (manager, WorkspaceRole.Manager),
            (developer, WorkspaceRole.Developer), (viewer, WorkspaceRole.Viewer));
        using var client = fixture.Client(owner);
        var project = await ProjectAsync(client, workspace.Id, "PRIVATE");
        foreach (var user in new[] { manager, developer, viewer })
        {
            using var restricted = fixture.Client(user);
            Assert.Equal(HttpStatusCode.Forbidden, (await restricted.PostAsJsonAsync(Projects(workspace.Id), new { name = "Denied", projectKey = "DENY" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await restricted.GetAsync(Project(workspace.Id, project.Id))).StatusCode);
            Assert.Equal(0, (await restricted.GetFromJsonAsync<ProjectPageDto>(Projects(workspace.Id)))!.Total);
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(Project(workspace.Id, project.Id) + "/members", new { userId = user.Id })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync(Project(workspace.Id, project.Id))).StatusCode);
            Assert.Equal(1, (await restricted.GetFromJsonAsync<ProjectPageDto>(Projects(workspace.Id)))!.Total);
        }
        using var adminClient = fixture.Client(admin);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync(Project(workspace.Id, project.Id))).StatusCode);
        using var managerClient = fixture.Client(manager);
        var latest = await client.GetFromJsonAsync<ProjectResponseDto>(Project(workspace.Id, project.Id));
        Assert.Equal(HttpStatusCode.OK, (await managerClient.PutAsJsonAsync(Project(workspace.Id, project.Id), Update(latest!, "Manager renamed"))).StatusCode);
        foreach (var user in new[] { developer, viewer })
        {
            using var restricted = fixture.Client(user);
            Assert.Equal(HttpStatusCode.Forbidden, (await restricted.PutAsJsonAsync(Project(workspace.Id, project.Id), Update(latest!))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await restricted.PostAsync(Project(workspace.Id, project.Id) + "/archive", null)).StatusCode);
        }
        var elsewhere = await fixture.WorkspaceAsync(outsider);
        using var outsiderClient = fixture.Client(outsider);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync(Project(workspace.Id, project.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Project(elsewhere.Id, project.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Project(workspace.Id, project.Id) + "/members", new { userId = outsider.Id })).StatusCode);
    }

    [PostgresFact]
    public async Task Project_validation_unique_keys_search_pagination_and_archive_behavior()
    {
        var owner = await fixture.UserAsync();
        var developer = await fixture.UserAsync();
        var outsider = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (developer, WorkspaceRole.Developer));
        using var client = fixture.Client(owner);
        var project = await ProjectAsync(client, workspace.Id, "FLOW");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Projects(workspace.Id), new { name = "Duplicate", projectKey = "flow" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Projects(workspace.Id), new { name = "Bad", projectKey = "BAD", leadUserId = outsider.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Projects(workspace.Id), new { name = "Bad", projectKey = "!" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Projects(workspace.Id), new { name = "Bad", projectKey = "DATE", startsOn = "2026-10-20", targetOn = "2026-10-01" })).StatusCode);
        var otherWorkspace = await fixture.WorkspaceAsync(owner);
        await ProjectAsync(client, otherWorkspace.Id, "FLOW");
        await ProjectAsync(client, workspace.Id, "SECOND");
        var page = await client.GetFromJsonAsync<ProjectPageDto>(Projects(workspace.Id) + "?search=flow&sort=key&pageSize=1&archived=false");
        Assert.Equal(1, page!.Total);
        Assert.Equal(project.Id, Assert.Single(page.Items).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Projects(workspace.Id) + "?sort=unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Projects(workspace.Id) + "?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(Project(workspace.Id, project.Id), Update(project, "Updated"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Project(workspace.Id, project.Id), Update(project, "Stale"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(Project(workspace.Id, project.Id) + "/archive", null)).StatusCode);
        var archived = await client.GetFromJsonAsync<ProjectResponseDto>(Project(workspace.Id, project.Id));
        Assert.NotNull(archived!.ArchivedAt);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Project(workspace.Id, project.Id), Update(archived))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Project(workspace.Id, project.Id) + "/members", new { userId = developer.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(Project(workspace.Id, project.Id) + "/restore", null)).StatusCode);
        var restored = await client.GetFromJsonAsync<ProjectResponseDto>(Project(workspace.Id, project.Id));
        Assert.Null(restored!.ArchivedAt);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(Project(workspace.Id, project.Id), Update(restored))).StatusCode);
    }

    [PostgresFact]
    public async Task Removing_members_preserves_history_and_removes_project_access_and_lead_assignments()
    {
        var owner = await fixture.UserAsync();
        var developer = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (developer, WorkspaceRole.Developer));
        using var client = fixture.Client(owner);
        using var developerClient = fixture.Client(developer);
        var response = await client.PostAsJsonAsync(Projects(workspace.Id), new CreateProjectRequestDto("Lead project", "LEAD", LeadUserId: developer.Id));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = (await response.Content.ReadFromJsonAsync<ProjectResponseDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await developerClient.GetAsync(Project(workspace.Id, project.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/workspaces/{workspace.Id}/members/{developer.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await developerClient.GetAsync(Project(workspace.Id, project.Id))).StatusCode);
        Assert.Null((await client.GetFromJsonAsync<ProjectResponseDto>(Project(workspace.Id, project.Id)))!.LeadUserId);
        await fixture.InDb(async db =>
        {
            Assert.NotNull((await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == workspace.Id && x.UserId == developer.Id)).RemovedAt);
            Assert.NotNull((await db.ProjectMemberships.SingleAsync(x => x.ProjectId == project.Id)).RemovedAt);
        });
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/members", new { email = developer.Email, role = "Developer" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await developerClient.GetAsync(Project(workspace.Id, project.Id))).StatusCode);
    }

    [PostgresFact]
    public async Task Concurrent_project_creation_and_membership_addition_preserve_unique_constraints()
    {
        var owner = await fixture.UserAsync();
        var user = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (user, WorkspaceRole.Developer));
        using var client = fixture.Client(owner);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(Projects(workspace.Id), new { name = "Concurrent", projectKey = "RACE" })));
        var created = Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var project = (await created.Content.ReadFromJsonAsync<ProjectResponseDto>())!;
        results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(Project(workspace.Id, project.Id) + "/members", new { userId = user.Id })));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
    }

    [PostgresFact]
    public async Task Database_rejects_cross_workspace_project_membership_and_leads()
    {
        var owner = await fixture.UserAsync();
        var other = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner);
        var elsewhere = await fixture.WorkspaceAsync(other);
        using var client = fixture.Client(owner);
        var project = await ProjectAsync(client, workspace.Id, "SCOPE");
        Guid wrongMembership = default;
        await fixture.InDb(async db => wrongMembership = await db.WorkspaceMemberships.Where(x => x.WorkspaceId == elsewhere.Id).Select(x => x.Id).SingleAsync());
        await fixture.InDb(async db =>
        {
            db.ProjectMemberships.Add(new ProjectMembership { ProjectId = project.Id, WorkspaceId = workspace.Id, WorkspaceMembershipId = wrongMembership });
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)exception.InnerException!).SqlState);
        });
        await fixture.InDb(async db =>
        {
            var stored = await db.Projects.SingleAsync(x => x.Id == project.Id);
            stored.LeadMembershipId = wrongMembership;
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)exception.InnerException!).SqlState);
        });
    }

    [PostgresFact]
    public async Task Workspace_creation_and_member_roles_preserve_the_owner_and_database_rejects_ownerless_creation()
    {
        var owner = await fixture.UserAsync();
        var admin = await fixture.UserAsync();
        var viewer = await fixture.UserAsync();
        using var client = fixture.Client(owner);
        using var anonymous = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/workspaces", new { name = "Denied" })).StatusCode);
        var created = await client.PostAsJsonAsync("/api/v1/workspaces", new { name = "Created workspace" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var workspace = (await created.Content.ReadFromJsonAsync<WorkspaceResponseDto>())!;
        Assert.Equal("Owner", workspace.Role);
        foreach (var (user, role) in new[] { (admin, "Admin"), (viewer, "Viewer") })
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/members", new { email = user.Email, role })).StatusCode);
        using var adminClient = fixture.Client(admin);
        using var viewerClient = fixture.Client(viewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.PutAsJsonAsync($"/api/v1/workspaces/{workspace.Id}", new { name = "Denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.DeleteAsync($"/api/v1/workspaces/{workspace.Id}/members/{owner.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/v1/workspaces/{workspace.Id}/members/{owner.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/members/{owner.Id}/role", new { role = "Admin" })).StatusCode);
        await ProjectAsync(adminClient, workspace.Id, "ADMIN");
        await fixture.InDb(async db =>
        {
            db.Workspaces.Add(new Workspace { Name = "No owner", CreatedByUserId = owner.Id });
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, ((PostgresException)exception.InnerException!).SqlState);
        });
    }

    [PostgresFact]
    public async Task Otp_attempt_limit_is_persisted_under_concurrent_wrong_guesses()
    {
        var user = await fixture.UserAsync(false);
        using var client = fixture.Client(user);
        var id = await SeedCodeAsync(user, "000123");
        var failures = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            client.PostAsJsonAsync("/api/v1/auth/email-verification/confirm", new { code = "999999" })));
        Assert.All(failures, response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/email-verification/confirm", new { code = "000123" })).StatusCode);
        await fixture.InDb(async db =>
        {
            var challenge = await db.EmailVerificationTokens.SingleAsync(x => x.Id == id);
            Assert.Equal(5, challenge.FailedAttempts);
            Assert.NotNull(challenge.UsedAt);
            Assert.Null((await db.Users.SingleAsync(x => x.Id == user.Id)).EmailVerifiedAt);
        });
        // Burning the attempts does not bypass the resend cooldown.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/v1/auth/email-verification/request", null)).StatusCode);
    }

    [PostgresFact]
    public async Task Resending_replaces_the_old_code_and_uses_a_ten_minute_expiry()
    {
        var user = await fixture.UserAsync(false);
        using var client = fixture.Client(user);
        var oldId = await SeedCodeAsync(user, "000123", DateTimeOffset.UtcNow.AddMinutes(-2));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/email-verification/request", null)).StatusCode);
        var current = fixture.Sender.Codes[user.Email];
        await fixture.InDb(async db =>
        {
            Assert.NotNull((await db.EmailVerificationTokens.SingleAsync(x => x.Id == oldId)).UsedAt);
            var active = await db.EmailVerificationTokens.SingleAsync(x => x.UserId == user.Id && x.UsedAt == null);
            Assert.Equal(TimeSpan.FromMinutes(10), active.ExpiresAt - active.CreatedAt);
            Assert.Equal(0, active.FailedAttempts);
            Assert.True(EmailVerificationCodes.Matches(current, active.Id, user.Id, PhaseTwoFixture.SigningKey, active.TokenHash));
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/api/v1/auth/email-verification/request", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/email-verification/confirm", new { code = current })).StatusCode);
    }

    [PostgresFact]
    public async Task Malformed_codes_count_as_attempts_and_leading_zero_codes_are_supported()
    {
        var user = await fixture.UserAsync(false);
        using var client = fixture.Client(user);
        var id = await SeedCodeAsync(user, "000123");
        foreach (var code in new string?[] { null, "123", "abcdef", "１２３４５６" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/email-verification/confirm", new { code })).StatusCode);
        await fixture.InDb(async db => Assert.Equal(4, (await db.EmailVerificationTokens.SingleAsync(x => x.Id == id)).FailedAttempts));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/email-verification/confirm", new { code = "000123" })).StatusCode);
    }

    [PostgresFact]
    public async Task Verified_accounts_do_not_receive_another_code_and_remain_verified_after_sign_in()
    {
        var user = await fixture.UserAsync(false);
        using var client = fixture.Client(user);
        await SeedCodeAsync(user, "012345");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/email-verification/confirm", new { code = "012345" })).StatusCode);
        DateTimeOffset? verifiedAt = null;
        await fixture.InDb(async db =>
        {
            var stored = await db.Users.SingleAsync(x => x.Id == user.Id);
            verifiedAt = stored.EmailVerifiedAt;
            stored.PasswordHash = FlowForge.Infrastructure.Identity.PasswordHashing.Hash("StrongPassword1!");
            await db.SaveChangesAsync();
        });
        // Verified users do not depend on SMTP availability anymore.
        fixture.Sender.IsConfigured = false;
        try { Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/email-verification/request", null)).StatusCode); }
        finally { fixture.Sender.IsConfigured = true; }
        Assert.False(fixture.Sender.Codes.ContainsKey(user.Email));
        using var signedOutClient = fixture.Client();
        var login = await signedOutClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDto(user.Email, "StrongPassword1!"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = (await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>())!;
        signedOutClient.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        Assert.True((await signedOutClient.GetFromJsonAsync<CurrentUserResponseDto>("/api/v1/auth/me"))!.EmailVerified);
        await fixture.InDb(async db => Assert.Equal(verifiedAt, (await db.Users.SingleAsync(x => x.Id == user.Id)).EmailVerifiedAt));
    }

    [PostgresFact]
    public async Task Feature_endpoints_return_field_validation_errors_after_authorizing_the_request()
    {
        var owner = await fixture.UserAsync();
        var outsider = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner);
        using var client = fixture.Client(owner);
        using var outsideClient = fixture.Client(outsider);
        var project = await ProjectAsync(client, workspace.Id, "VALID");
        var requests = new (HttpMethod Method, string Path, object? Body, string[] Fields)[]
        {
            (HttpMethod.Post, "/api/v1/workspaces", new { name = "" }, ["name"]),
            (HttpMethod.Put, $"/api/v1/workspaces/{workspace.Id}", new { name = "" }, ["name"]),
            (HttpMethod.Post, $"/api/v1/workspaces/{workspace.Id}/members", new { email = "bad", role = "1" }, ["email", "role"]),
            (HttpMethod.Put, $"/api/v1/workspaces/{workspace.Id}/members/{owner.Id}/role", new { role = "unknown" }, ["role"]),
            (HttpMethod.Post, $"/api/v1/workspaces/{workspace.Id}/ownership/transfer", new { newOwnerUserId = Guid.Empty }, ["newOwnerUserId"]),
            (HttpMethod.Post, $"/api/v1/workspaces/{workspace.Id}/invitations", new { email = "bad", role = "unknown" }, ["email", "role"]),
            (HttpMethod.Post, "/api/v1/invitations/accept", new { token = "" }, ["token"]),
            (HttpMethod.Post, Projects(workspace.Id), new { name = "", projectKey = "!" }, ["name", "projectKey"]),
            (HttpMethod.Put, Project(workspace.Id, project.Id), new { name = "Valid", projectKey = "VALID", version = 0 }, ["version"]),
            (HttpMethod.Post, Project(workspace.Id, project.Id) + "/members", new { userId = Guid.Empty }, ["userId"]),
            (HttpMethod.Get, Projects(workspace.Id) + "?page=0&sort=unsafe", null, ["page", "sort"]),
            (HttpMethod.Get, Project(workspace.Id, project.Id) + "/members?pageSize=101", null, ["pageSize"]),
            (HttpMethod.Get, $"/api/v1/workspaces/{workspace.Id}/invitations?page=0", null, ["page"])
        };
        foreach (var (method, path, body, fields) in requests)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body != null) request.Content = JsonContent.Create(body);
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var errors = json.RootElement.GetProperty("errors");
            foreach (var field in fields) Assert.True(errors.TryGetProperty(field, out _), field);
        }
        // Resource authorization still precedes validation and does not leak data.
        Assert.Equal(HttpStatusCode.NotFound, (await outsideClient.PostAsJsonAsync(Projects(workspace.Id), new { name = "", projectKey = "!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsideClient.GetAsync(Projects(workspace.Id) + "?page=0")).StatusCode);
    }

    private async Task<Guid> SeedCodeAsync(User user, string code, DateTimeOffset? createdAt = null)
    {
        var challenge = new EmailVerificationToken { UserId = user.Id, NormalizedEmail = user.NormalizedEmail,
            TokenHash = "", CreatedAt = createdAt ?? DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10) };
        challenge.TokenHash = EmailVerificationCodes.Hash(code, challenge.Id, user.Id, PhaseTwoFixture.SigningKey);
        await fixture.InDb(async db => { db.EmailVerificationTokens.Add(challenge); await db.SaveChangesAsync(); });
        return challenge.Id;
    }

    private static string Projects(Guid workspaceId) => $"/api/v1/workspaces/{workspaceId}/projects";
    private static string Project(Guid workspaceId, Guid projectId) => Projects(workspaceId) + $"/{projectId}";
    private static UpdateProjectRequestDto Update(ProjectResponseDto project, string? name = null) =>
        new(name ?? project.Name, project.ProjectKey, project.Description, project.LeadUserId, project.StartsOn, project.TargetOn, project.Version);
    private static async Task<ProjectResponseDto> ProjectAsync(HttpClient client, Guid workspaceId, string key)
    {
        var response = await client.PostAsJsonAsync(Projects(workspaceId), new CreateProjectRequestDto(key + " project", key));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProjectResponseDto>())!;
    }
    private static async Task<CreatedInvitationResponseDto> InviteAsync(HttpClient client, Guid workspaceId, string email)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/invitations", new CreateInvitationRequestDto(email, "Developer"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedInvitationResponseDto>())!;
    }
}
