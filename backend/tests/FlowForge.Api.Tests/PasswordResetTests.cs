using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FlowForge.Api.Contracts.Identity;
using FlowForge.Domain.Identity;
using FlowForge.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PasswordResetTests(PhaseTwoFixture fixture) : IClassFixture<PhaseTwoFixture>
{
    private const string Password = "ChangedPassword1!";
    private static Task<HttpResponseMessage> Request(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email });
    private static Task<HttpResponseMessage> Verify(HttpClient client, string email, string code) =>
        client.PostAsJsonAsync("/api/v1/auth/forgot-password/verify", new { email, code });
    private static Task<HttpResponseMessage> Reset(HttpClient client, string resetToken, string password = Password) =>
        client.PostAsJsonAsync("/api/v1/auth/reset-password", new { resetToken, password });
    private string Code(User user) => Regex.Match(fixture.Sender.Messages.Last(x => x.Recipient == user.Email).Body, @"\b\d{6}\b").Value;
    private async Task<string> Grant(HttpClient client, User user)
    {
        Assert.Equal(HttpStatusCode.NoContent, (await Request(client, user.Email)).StatusCode);
        var response = await Verify(client, user.Email, Code(user));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PasswordResetTokenResponseDto>())!.ResetToken;
    }

    [PostgresFact]
    public async Task Anonymous_recovery_changes_only_password_and_revokes_all_refresh_families()
    {
        using var client = fixture.Client();
        var registration = await client.PostAsJsonAsync("/api/v1/auth/register", new { email = Guid.NewGuid() + "@example.com", password = "OriginalPassword1!", displayName = "Recovery" });
        var original = (await registration.Content.ReadFromJsonAsync<AuthTokenResponseDto>())!;
        var user = new User { Email = "", NormalizedEmail = "", PasswordHash = "", DisplayName = "" };
        await fixture.InDb(async db => user = await db.Users.SingleAsync(x => x.DisplayName == "Recovery"));
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "OriginalPassword1!" });
        var second = (await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>())!;
        var token = await Grant(client, user);
        Assert.Equal(HttpStatusCode.NoContent, (await Reset(client, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(client, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "OriginalPassword1!" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = Password })).StatusCode);
        foreach (var old in new[] { original, second })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = old.RefreshToken })).StatusCode);
        await fixture.InDb(async db =>
        {
            var stored = await db.Users.SingleAsync(x => x.Id == user.Id);
            Assert.Null(stored.EmailVerifiedAt);
            Assert.Equal(user.DisplayName, stored.DisplayName);
            Assert.Equal(user.Email, stored.Email);
            Assert.True(stored.IsActive);
        });
    }

    [PostgresFact]
    public async Task Request_is_generic_normalizes_email_and_silently_limits_resends()
    {
        var user = await fixture.UserAsync();
        var inactive = await fixture.UserAsync();
        await fixture.InDb(async db => await db.Users.Where(x => x.Id == inactive.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)));
        using var client = fixture.Client();
        foreach (var email in new[] { "missing@example.com", inactive.Email, "  " + user.Email.ToUpperInvariant() + "  ", user.Email })
        {
            var response = await Request(client, email);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal("", await response.Content.ReadAsStringAsync());
        }
        Assert.Single(fixture.Sender.Messages, x => x.Recipient == user.Email);
        Assert.DoesNotContain(fixture.Sender.Messages, x => x.Recipient == inactive.Email);
        await fixture.InDb(async db =>
        {
            var challenge = await db.PasswordResetChallenges.SingleAsync(x => x.UserId == user.Id);
            Assert.NotEqual(Code(user), challenge.CodeHash);
            Assert.Equal(64, challenge.CodeHash.Length);
            Assert.Null(challenge.ResetTokenHash);
        });
    }

    [PostgresFact]
    public async Task Expired_wrong_and_malformed_codes_cannot_issue_a_token_and_lock_after_five_attempts()
    {
        var user = await fixture.UserAsync();
        using var client = fixture.Client();
        await Request(client, user.Email);
        var code = Code(user);
        foreach (var wrong in new[] { "bad", "", "12345", "abcdef", code == "999999" ? "888888" : "999999" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Verify(client, user.Email, wrong)).StatusCode);
        using var retry = fixture.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(retry, user.Email, code)).StatusCode);
        await fixture.InDb(async db =>
        {
            var challenge = await db.PasswordResetChallenges.SingleAsync(x => x.UserId == user.Id);
            Assert.Equal(5, challenge.FailedAttempts);
            Assert.NotNull(challenge.UsedAt);
            Assert.Null(challenge.ResetTokenHash);
        });
        var expired = await fixture.UserAsync();
        await Request(retry, expired.Email);
        await fixture.InDb(async db => await db.PasswordResetChallenges.Where(x => x.UserId == expired.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(retry, expired.Email, Code(expired))).StatusCode);
    }

    [PostgresFact]
    public async Task Confirmation_is_bound_to_account_purpose_and_is_single_use_even_concurrently()
    {
        var user = await fixture.UserAsync(false);
        var other = await fixture.UserAsync();
        using var client = fixture.Client();
        await Request(client, user.Email);
        var code = Code(user);
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(client, other.Email, code)).StatusCode);
        using var authenticated = fixture.Client(user);
        Assert.Equal(HttpStatusCode.BadRequest, (await authenticated.PostAsJsonAsync("/api/v1/auth/email-verification/confirm", new { code })).StatusCode);
        var replies = await Task.WhenAll(Verify(client, user.Email, code), Verify(client, user.Email, code));
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.BadRequest);
        var grant = (await replies.Single(x => x.IsSuccessStatusCode).Content.ReadFromJsonAsync<PasswordResetTokenResponseDto>())!;
        await fixture.InDb(async db => Assert.Equal(SecureTokens.Hash(grant.ResetToken),
            (await db.PasswordResetChallenges.SingleAsync(x => x.UserId == user.Id)).ResetTokenHash));
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(client, code)).StatusCode);
    }

    [PostgresFact]
    public async Task Resending_invalidates_old_otp_and_reset_grant_but_delivery_failure_rolls_back()
    {
        var user = await fixture.UserAsync();
        using var client = fixture.Client();
        var token = await Grant(client, user);
        await AgeRequest(user);
        fixture.Sender.FailDelivery = true;
        try { Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Request(client, user.Email)).StatusCode); }
        finally { fixture.Sender.FailDelivery = false; }
        await fixture.InDb(async db => Assert.Null((await db.PasswordResetChallenges.SingleAsync(x => x.UserId == user.Id)).UsedAt));
        Assert.Equal(HttpStatusCode.NoContent, (await Request(client, user.Email)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(client, token)).StatusCode);
        // Use a deterministically different guess so this assertion cannot fail on a random collision.
        var currentCode = Code(user);
        var wrong = currentCode == "000123" ? "000124" : "000123";
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(client, user.Email, wrong)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Verify(client, user.Email, currentCode)).StatusCode);
    }

    [PostgresFact]
    public async Task Weak_password_does_not_consume_grant_and_parallel_resets_have_one_winner()
    {
        var user = await fixture.UserAsync();
        using var client = fixture.Client();
        var token = await Grant(client, user);
        var weak = await Reset(client, token, "weak");
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("password", await weak.Content.ReadAsStringAsync());
        var replies = await Task.WhenAll(Reset(client, token), Reset(client, token, "OtherPassword1!"));
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(replies, x => x.StatusCode == HttpStatusCode.BadRequest);
    }

    [PostgresFact]
    public async Task Expired_or_inactive_or_email_changed_grants_cannot_reset()
    {
        using var client = fixture.Client();
        foreach (var mode in new[] { "expired", "inactive", "email" })
        {
            var user = await fixture.UserAsync();
            var token = await Grant(client, user);
            await fixture.InDb(async db =>
            {
                if (mode == "expired") await db.PasswordResetChallenges.Where(x => x.UserId == user.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ResetTokenExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
                else if (mode == "inactive") await db.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
                else await db.Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NormalizedEmail, "CHANGED@EXAMPLE.COM"));
            });
            Assert.Equal(HttpStatusCode.BadRequest, (await Reset(client, token)).StatusCode);
        }
    }

    [PostgresFact]
    public async Task Leading_zero_code_is_preserved_and_smtp_configuration_is_required()
    {
        var user = await fixture.UserAsync();
        using var client = fixture.Client();
        fixture.Sender.IsConfigured = false;
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Request(client, user.Email)).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Request(client, "missing@example.com")).StatusCode);
        }
        finally { fixture.Sender.IsConfigured = true; }
        await Request(client, user.Email);
        await fixture.InDb(async db =>
        {
            var challenge = await db.PasswordResetChallenges.SingleAsync(x => x.UserId == user.Id);
            challenge.CodeHash = EmailVerificationCodes.Hash("password-reset:000123", challenge.Id, user.Id, PhaseTwoFixture.SigningKey);
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await Verify(client, user.Email, "000123")).StatusCode);
    }

    [PostgresFact]
    public async Task All_recovery_routes_validate_input_and_share_an_ip_rate_limit()
    {
        using var client = fixture.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await Request(client, "bad")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Verify(client, "bad", "123456")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(client, "")).StatusCode);
        for (var i = 0; i < 7; i++) Assert.Equal(HttpStatusCode.NoContent, (await Request(client, "missing@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Request(client, "missing@example.com")).StatusCode);
    }

    [PostgresFact]
    public async Task Concurrent_login_refresh_and_reset_leave_no_old_password_sessions_alive()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var user = await fixture.UserAsync();
            await fixture.InDb(async db => await db.Users.Where(x => x.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.PasswordHash, PasswordHashing.Hash("OriginalPassword1!"))));
            using var client = fixture.Client();
            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "OriginalPassword1!" });
            var original = (await login.Content.ReadFromJsonAsync<AuthTokenResponseDto>())!;
            var token = await Grant(client, user);
            var concurrentLogin = client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = "OriginalPassword1!" });
            var concurrentRefresh = client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = original.RefreshToken });
            var reset = Reset(client, token);
            await Task.WhenAll(concurrentLogin, concurrentRefresh, reset);
            Assert.Equal(HttpStatusCode.NoContent, reset.Result.StatusCode);
            foreach (var reply in new[] { concurrentLogin.Result, concurrentRefresh.Result })
            {
                Assert.Contains(reply.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized });
                if (reply.IsSuccessStatusCode)
                {
                    var issued = (await reply.Content.ReadFromJsonAsync<AuthTokenResponseDto>())!;
                    Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = issued.RefreshToken })).StatusCode);
                }
            }
            await fixture.InDb(async db => Assert.False(await db.RefreshSessions.AnyAsync(x => x.UserId == user.Id && x.RevokedAt == null)));
        }
    }

    private Task AgeRequest(User user) => fixture.InDb(async db => await db.PasswordResetChallenges.Where(x => x.UserId == user.Id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTimeOffset.UtcNow.AddMinutes(-2))));
}
