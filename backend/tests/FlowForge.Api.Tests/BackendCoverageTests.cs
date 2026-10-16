using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FlowForge.Api.Contracts.Identity;
using FlowForge.Api.Contracts.Projects;
using FlowForge.Api.Contracts.Workspaces;
using FlowForge.Domain.Workspaces;
using FlowForge.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowForge.Api.Tests;

// Complements the phase-two tests with session, rate-limit, CRUD, and query checks.
public sealed class BackendCoverageTests(PhaseTwoFixture fixture) : IClassFixture<PhaseTwoFixture>
{
    private const string Password = "BackendTest1!";

    [PostgresFact]
    public async Task Registration_normalizes_identity_and_persists_only_password_and_refresh_hashes()
    {
        using var client = fixture.Client();
        var email = Guid.NewGuid().ToString("N") + "@example.com";
        var tokens = await RegisterAsync(client, " " + email + " ");
        client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
        var me = await client.GetFromJsonAsync<CurrentUserResponseDto>("/api/v1/auth/me");
        Assert.Equal(email, me!.Email);
        Assert.Equal("Backend Tester", me.DisplayName);
        Assert.False(me.EmailVerified);
        Assert.InRange(tokens.AccessTokenExpiresAt, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));
        await fixture.InDb(async db =>
        {
            var user = await db.Users.SingleAsync(x => x.Id == me.Id);
            Assert.Equal(email.ToUpperInvariant(), user.NormalizedEmail);
            Assert.NotEqual(Password, user.PasswordHash);
            Assert.True(PasswordHashing.Verify(Password, user.PasswordHash));
            var session = await db.RefreshSessions.SingleAsync(x => x.UserId == user.Id);
            Assert.NotEqual(tokens.RefreshToken, session.TokenHash);
            Assert.Equal(SecureTokens.Hash(tokens.RefreshToken), session.TokenHash);
            Assert.InRange(session.ExpiresAt, DateTimeOffset.UtcNow.AddDays(29), DateTimeOffset.UtcNow.AddDays(31));
        });
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequestDto(email.ToUpperInvariant(), Password, "Duplicate"))).StatusCode);
        Assert.False(fixture.Sender.Codes.ContainsKey(email));
    }

    [PostgresFact]
    public async Task Login_refresh_replay_and_logout_enforce_session_family_boundaries()
    {
        using var client = fixture.Client();
        var email = Guid.NewGuid().ToString("N") + "@example.com";
        var initial = await RegisterAsync(client, email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Wrong1!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "missing@example.com", password = Password })).StatusCode);
        var login = await ReadAsync<AuthTokenResponseDto>(await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = " " + email.ToUpperInvariant() + " ", password = Password }), HttpStatusCode.OK);
        var rotated = await ReadAsync<AuthTokenResponseDto>(await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = initial.RefreshToken }), HttpStatusCode.OK);
        Assert.NotEqual(initial.RefreshToken, rotated.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = initial.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated.RefreshToken })).StatusCode);
        var independent = await ReadAsync<AuthTokenResponseDto>(await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = login.RefreshToken }), HttpStatusCode.OK);
        // Logging out with the old token also revokes its replacement, but not access JWTs.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = login.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = independent.RefreshToken })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", independent.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        foreach (var token in new string?[] { login.RefreshToken, "unknown", "", null })
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = token })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = new string('x', 257) })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "unknown" })).StatusCode);
    }

    [PostgresFact]
    public async Task Expired_refresh_and_inactive_accounts_cannot_authenticate()
    {
        using var client = fixture.Client();
        var email = Guid.NewGuid().ToString("N") + "@example.com";
        var tokens = await RegisterAsync(client, email);
        await fixture.InDb(async db =>
        {
            var session = await db.RefreshSessions.SingleAsync(x => x.TokenHash == SecureTokens.Hash(tokens.RefreshToken));
            session.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken })).StatusCode);
        var login = await ReadAsync<AuthTokenResponseDto>(await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password }), HttpStatusCode.OK);
        await fixture.InDb(async db =>
        {
            (await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant())).IsActive = false;
            await db.SaveChangesAsync();
        });
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.RefreshToken })).StatusCode);
    }

    [PostgresFact]
    public async Task Protected_endpoint_groups_reject_missing_expired_and_invalid_tokens()
    {
        var user = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(user);
        var paths = new[] { "/api/v1/auth/me", "/api/v1/workspaces", $"/api/v1/workspaces/{workspace.Id}",
            $"/api/v1/workspaces/{workspace.Id}/projects", $"/api/v1/workspaces/{workspace.Id}/invitations", $"/api/v1/invitations/{Guid.NewGuid()}" };
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(PhaseTwoFixture.SigningKey)),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        foreach (var token in new string?[] { null, "nonsense",
            new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("flowforge", "flowforge-api",
                [new Claim("sub", user.Id.ToString())], expires: DateTime.UtcNow.AddMinutes(-1), signingCredentials: credentials)),
            new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("wrong-issuer", "flowforge-api",
                [new Claim("sub", user.Id.ToString())], expires: DateTime.UtcNow.AddMinutes(1), signingCredentials: credentials)),
            new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("flowforge", "wrong-audience",
                [new Claim("sub", user.Id.ToString())], expires: DateTime.UtcNow.AddMinutes(1), signingCredentials: credentials)) })
        {
            using var client = fixture.Client();
            if (token != null) client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            foreach (var path in paths)
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/email-verification/request", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/invitations/accept", new { token = "unknown" })).StatusCode);
        }
    }

    [PostgresFact]
    public async Task Rate_limits_apply_per_client_and_refresh_logout_share_a_budget()
    {
        foreach (var (path, limit) in new[] { ("register", 5), ("login", 10), ("refresh", 20) })
        {
            using var client = fixture.Client();
            for (var i = 0; i < limit; i++)
            {
                var target = path == "refresh" && i % 2 == 0 ? "logout" : path;
                Assert.Equal(target == "logout" ? HttpStatusCode.NoContent : HttpStatusCode.BadRequest,
                    (await client.PostAsJsonAsync("/api/v1/auth/" + target, new { })).StatusCode);
            }
            Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/v1/auth/" + path, new { })).StatusCode);
            using var otherIp = fixture.Client();
            Assert.Equal(HttpStatusCode.BadRequest, (await otherIp.PostAsJsonAsync("/api/v1/auth/" + path, new { })).StatusCode);
        }
        var user = await fixture.UserAsync();
        using var verified = fixture.Client(user);
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await verified.PostAsync("/api/v1/auth/email-verification/request", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await verified.PostAsJsonAsync("/api/v1/auth/email-verification/confirm", new { code = "000000" })).StatusCode);
    }

    [PostgresFact]
    public async Task Workspace_reads_renames_role_changes_and_removal_use_current_membership()
    {
        var owner = await fixture.UserAsync();
        var admin = await fixture.UserAsync();
        var member = await fixture.UserAsync();
        var outsider = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (admin, WorkspaceRole.Admin), (member, WorkspaceRole.Viewer));
        using var client = fixture.Client(owner);
        using var adminClient = fixture.Client(admin);
        using var memberClient = fixture.Client(member);
        using var outsiderClient = fixture.Client(outsider);
        var path = $"/api/v1/workspaces/{workspace.Id}";
        var renamed = await ReadAsync<WorkspaceResponseDto>(await adminClient.PutAsJsonAsync(path, new { name = "  Renamed  " }), HttpStatusCode.OK);
        Assert.Equal("Renamed", renamed.Name);
        Assert.Equal("Admin", renamed.Role);
        Assert.Equal("Renamed", (await memberClient.GetFromJsonAsync<WorkspaceResponseDto>(path))!.Name);
        Assert.Contains((await memberClient.GetFromJsonAsync<WorkspaceResponseDto[]>("/api/v1/workspaces"))!, x => x.Id == workspace.Id);
        Assert.Equal(3, (await memberClient.GetFromJsonAsync<WorkspaceMemberResponseDto[]>(path + "/members"))!.Length);
        Assert.Empty((await outsiderClient.GetFromJsonAsync<WorkspaceResponseDto[]>("/api/v1/workspaces"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync(path + "/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PutAsJsonAsync(path, new { name = "Denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(path + $"/members/{member.Id}/role", new { role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await memberClient.PutAsJsonAsync(path, new { name = "New authority" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.PutAsJsonAsync(path + $"/members/{member.Id}/role", new { role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(path + $"/members/{member.Id}/role", new { role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await memberClient.PutAsJsonAsync(path, new { name = "Denied again" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path + "/members", new { email = member.Email, role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(path + "/members", new { email = "absent@example.com", role = "Viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await adminClient.DeleteAsync(path + $"/members/{member.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync(path)).StatusCode);
        Assert.Empty((await memberClient.GetFromJsonAsync<WorkspaceResponseDto[]>("/api/v1/workspaces"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(path + $"/members/{member.Id}")).StatusCode);
    }

    [PostgresFact]
    public async Task Project_lead_changes_member_removal_and_archive_idempotency_preserve_versions_and_history()
    {
        var owner = await fixture.UserAsync();
        var first = await fixture.UserAsync();
        var second = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (first, WorkspaceRole.Developer), (second, WorkspaceRole.Viewer));
        using var client = fixture.Client(owner);
        using var firstClient = fixture.Client(first);
        var root = $"/api/v1/workspaces/{workspace.Id}/projects";
        var project = await ReadAsync<ProjectResponseDto>(await client.PostAsJsonAsync(root,
            new CreateProjectRequestDto("  Project  ", " lead ", " description ", first.Id, new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 16))), HttpStatusCode.Created);
        Assert.Equal("LEAD", project.ProjectKey);
        Assert.Equal("Project", project.Name);
        Assert.Equal("description", project.Description);
        var path = root + "/" + project.Id;
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(path + $"/members/{first.Id}")).StatusCode);
        var updated = await ReadAsync<ProjectResponseDto>(await client.PutAsJsonAsync(path,
            new UpdateProjectRequestDto("Changed", "LEAD", null, second.Id, null, null, project.Version)), HttpStatusCode.OK);
        Assert.Equal(project.Version + 1, updated.Version);
        Assert.Null(updated.Description);
        Assert.Null(updated.StartsOn);
        var members = await client.GetFromJsonAsync<JsonElement>(path + "/members?pageSize=1");
        Assert.Equal(2, members.GetProperty("total").GetInt32());
        Assert.Equal(1, members.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await firstClient.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(path + $"/members/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await firstClient.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(path + $"/members/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(path + "/members", new { userId = first.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await firstClient.GetAsync(path)).StatusCode);
        var archived = await ReadAsync<ProjectResponseDto>(await client.PostAsync(path + "/archive", null), HttpStatusCode.OK);
        var repeated = await ReadAsync<ProjectResponseDto>(await client.PostAsync(path + "/archive", null), HttpStatusCode.OK);
        Assert.Equal(archived.Version, repeated.Version);
        // PostgreSQL stores microseconds; the initial response can retain sub-microsecond ticks.
        Assert.InRange((archived.ArchivedAt!.Value - repeated.ArchivedAt!.Value).Duration(), TimeSpan.Zero, TimeSpan.FromMicroseconds(1));
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(path + $"/members/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path + "/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(root, new { name = "Duplicate archived", projectKey = "lead" })).StatusCode);
        var restored = await ReadAsync<ProjectResponseDto>(await client.PostAsync(path + "/restore", null), HttpStatusCode.OK);
        var restoredAgain = await ReadAsync<ProjectResponseDto>(await client.PostAsync(path + "/restore", null), HttpStatusCode.OK);
        Assert.Null(restored.ArchivedAt);
        Assert.Equal(archived.Version + 1, restored.Version);
        Assert.Equal(restored.Version, restoredAgain.Version);
        await fixture.InDb(async db =>
        {
            var rows = await db.ProjectMemberships.Where(x => x.ProjectId == project.Id && x.WorkspaceMembership.UserId == first.Id).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Single(rows, x => x.RemovedAt == null);
            Assert.Single(rows, x => x.RemovedAt != null);
        });
    }

    [PostgresFact]
    public async Task Project_filters_sorts_and_pagination_return_only_matching_authorized_rows()
    {
        var owner = await fixture.UserAsync();
        var lead = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (lead, WorkspaceRole.Developer));
        using var client = fixture.Client(owner);
        using var leadClient = fixture.Client(lead);
        var root = $"/api/v1/workspaces/{workspace.Id}/projects";
        var alpha = await ReadAsync<ProjectResponseDto>(await client.PostAsJsonAsync(root,
            new CreateProjectRequestDto("Alpha", "AL", LeadUserId: lead.Id)), HttpStatusCode.Created);
        var beta = await ReadAsync<ProjectResponseDto>(await client.PostAsJsonAsync(root,
            new CreateProjectRequestDto("Beta", "BE")), HttpStatusCode.Created);
        await client.PostAsync(root + $"/{beta.Id}/archive", null);
        foreach (var sort in new[] { "name", "key", "createdAt", "updatedAt" })
        {
            var asc = (await client.GetFromJsonAsync<ProjectPageDto>(root + $"?sort={sort}"))!;
            var desc = (await client.GetFromJsonAsync<ProjectPageDto>(root + $"?sort={sort}&descending=true"))!;
            Assert.Equal(2, asc.Total);
            Assert.Equal(asc.Items.Select(x => x.Id).Reverse(), desc.Items.Select(x => x.Id));
        }
        Assert.Equal(alpha.Id, Assert.Single((await client.GetFromJsonAsync<ProjectPageDto>(root + "?search=ALpHa&archived=false"))!.Items).Id);
        Assert.Equal(beta.Id, Assert.Single((await client.GetFromJsonAsync<ProjectPageDto>(root + "?archived=true"))!.Items).Id);
        Assert.Equal(alpha.Id, Assert.Single((await client.GetFromJsonAsync<ProjectPageDto>(root + $"?leadUserId={lead.Id}"))!.Items).Id);
        var secondPage = (await client.GetFromJsonAsync<ProjectPageDto>(root + "?page=2&pageSize=1"))!;
        Assert.Equal(2, secondPage.Total);
        Assert.Equal(beta.Id, Assert.Single(secondPage.Items).Id);
        Assert.Empty((await client.GetFromJsonAsync<ProjectPageDto>(root + "?page=1000000&pageSize=100"))!.Items);
        var restricted = (await leadClient.GetFromJsonAsync<ProjectPageDto>(root))!;
        Assert.Equal(1, restricted.Total);
        Assert.Equal(alpha.Id, Assert.Single(restricted.Items).Id);
    }

    [PostgresFact]
    public async Task Invitation_pages_terminal_states_and_admin_role_restrictions_are_enforced()
    {
        var owner = await fixture.UserAsync();
        var admin = await fixture.UserAsync();
        var recipient = await fixture.UserAsync();
        var unverified = await fixture.UserAsync(false);
        var workspace = await fixture.WorkspaceAsync(owner, (admin, WorkspaceRole.Admin));
        using var client = fixture.Client(owner);
        using var adminClient = fixture.Client(admin);
        using var recipientClient = fixture.Client(recipient);
        using var unverifiedClient = fixture.Client(unverified);
        var path = $"/api/v1/workspaces/{workspace.Id}/invitations";
        var invitation = await ReadAsync<CreatedInvitationResponseDto>(await client.PostAsJsonAsync(path,
            new { email = recipient.Email, role = "Admin" }), HttpStatusCode.Created);
        var pending = await ReadAsync<CreatedInvitationResponseDto>(await client.PostAsJsonAsync(path,
            new { email = unverified.Email, role = "Viewer" }), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.PostAsync(path + $"/{invitation.Invitation.Id}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unverifiedClient.PostAsJsonAsync("/api/v1/invitations/reject", new { token = pending.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await unverifiedClient.GetAsync($"/api/v1/invitations/{pending.Invitation.Id}")).StatusCode);
        var page = await client.GetFromJsonAsync<JsonElement>(path + "?pageSize=1");
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        var end = await client.GetFromJsonAsync<JsonElement>(path + "?page=1000000");
        Assert.Equal(0, end.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await recipientClient.PostAsJsonAsync("/api/v1/invitations/reject", new { token = invitation.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await recipientClient.PostAsJsonAsync("/api/v1/invitations/reject", new { token = invitation.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await recipientClient.PostAsJsonAsync("/api/v1/invitations/accept", new { token = invitation.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(path + $"/{invitation.Invitation.Id}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(path + $"/{Guid.NewGuid()}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await adminClient.PostAsync(path + $"/{pending.Invitation.Id}/revoke", null)).StatusCode);
    }

    [PostgresFact]
    public async Task Healthy_database_is_reported_ready_and_invalid_routes_are_rejected()
    {
        var owner = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner);
        using var client = fixture.Client(owner);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        var path = $"/api/v1/workspaces/{workspace.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/workspaces/not-a-uuid")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/workspaces/{Guid.NewGuid()}")).StatusCode);
    }

    [PostgresTheory]
    [InlineData("{")]
    [InlineData("{\"name\":123}")]
    [InlineData("null")]
    public async Task Malformed_json_returns_a_client_error_without_mutating_the_workspace(string body)
    {
        var owner = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner);
        using var client = fixture.Client(owner);
        var path = $"/api/v1/workspaces/{workspace.Id}";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await client.PutAsync(path, content);
        Assert.Equal(workspace.Name, (await client.GetFromJsonAsync<WorkspaceResponseDto>(path))!.Name);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [PostgresTheory]
    [InlineData("archived=invalid")]
    [InlineData("page=abc")]
    [InlineData("leadUserId=not-a-uuid")]
    public async Task Invalid_query_value_types_return_a_client_error(string query)
    {
        var owner = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner);
        using var client = fixture.Client(owner);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync($"/api/v1/workspaces/{workspace.Id}/projects?{query}")).StatusCode);
    }

    private static async Task<AuthTokenResponseDto> RegisterAsync(HttpClient client, string email) =>
        await ReadAsync<AuthTokenResponseDto>(await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequestDto(email, Password, "  Backend Tester  ")), HttpStatusCode.Created);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
