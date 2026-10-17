using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FlowForge.Api.Contracts.Identity;
using FlowForge.Api.Contracts.WorkItems;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class SystemAuditTests(PhaseFourFixture fixture) : IClassFixture<PhaseFourFixture>
{
    [PostgresTheory]
    [InlineData(7)]
    [InlineData(-5)]
    public async Task Time_entries_accept_offset_timestamps_and_persist_the_same_instant_in_utc(int offsetHours)
    {
        var journey = new PhaseFourIntegrationTests(fixture);
        var (owner, _, _, path) = await journey.SetupAsync();
        using var client = fixture.Client(owner);
        var item = await PhaseFourIntegrationTests.CreateItemAsync(client, path, null);
        var started = new DateTimeOffset(2026, 10, 17, 14, 30, 0, TimeSpan.FromHours(offsetHours));
        var entryPath = path + $"/work-items/{item.Id}/time-entries";
        var created = await client.PostAsJsonAsync(entryPath, new TimeEntryWriteDto(30, started, "Offset timestamp"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var entry = (await created.Content.ReadFromJsonAsync<TimeEntryDto>())!;
        Assert.Equal(started.ToUniversalTime(), entry.StartedAt);
        Assert.Equal(TimeSpan.Zero, entry.StartedAt!.Value.Offset);
        var updatedTime = started.AddHours(1);
        var updated = await client.PutAsJsonAsync(entryPath + $"/{entry.Id}", new TimeEntryWriteDto(45, updatedTime, "Updated"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        await fixture.InDb(async db =>
        {
            var saved = await db.TimeEntries.SingleAsync(x => x.Id == entry.Id);
            Assert.Equal(updatedTime.ToUniversalTime(), saved.StartedAt);
            Assert.Equal(TimeSpan.Zero, saved.StartedAt!.Value.Offset);
        });
    }

    [PostgresTheory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Signed_tokens_with_missing_malformed_or_empty_subject_are_rejected_at_authentication(string? subject)
    {
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(PhaseTwoFixture.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var claims = subject == null ? Array.Empty<Claim>() : [new Claim(JwtRegisteredClaimNames.Sub, subject)];
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("flowforge", "flowforge-api", claims,
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: credentials));
        using var client = fixture.Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        foreach (var path in new[] { "/api/v1/auth/me", "/api/v1/workspaces", "/api/v1/notifications" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/hubs/updates/negotiate?negotiateVersion=1", null)).StatusCode);
    }

    [PostgresFact]
    public async Task Logout_revokes_a_replacement_token_when_refresh_commits_concurrently()
    {
        using var client = fixture.Client();
        var registration = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = Guid.NewGuid().ToString("N") + "@example.com", password = "Audit-test-Password1!", displayName = "Audit"
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var tokens = (await registration.Content.ReadFromJsonAsync<AuthTokenResponseDto>())!;
        var configuration = fixture.Services.GetRequiredService<IConfiguration>();
        var connection = configuration.GetConnectionString("FlowForge")!;
        var commit = new PauseCommitInterceptor();
        await using var refreshingDb = new FlowForgeDbContext(new DbContextOptionsBuilder<FlowForgeDbContext>()
            .UseNpgsql(connection).AddInterceptors(commit).Options);
        await using var logoutDb = new FlowForgeDbContext(new DbContextOptionsBuilder<FlowForgeDbContext>().UseNpgsql(connection).Options);
        await logoutDb.Database.OpenConnectionAsync();
        var logoutPid = ((NpgsqlConnection)logoutDb.Database.GetDbConnection()).ProcessID;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var refreshing = new AuthService(refreshingDb, configuration).RefreshAsync(tokens.RefreshToken, timeout.Token);
        Task? loggingOut = null;
        try
        {
            await commit.Started.Task.WaitAsync(timeout.Token);
            loggingOut = new AuthService(logoutDb, configuration).LogoutAsync(tokens.RefreshToken, timeout.Token);
            // Wait for PostgreSQL to confirm logout is actually blocked by the uncommitted
            // refresh. This reproduces the race without timing-dependent sleeps.
            await using var monitor = new NpgsqlConnection(connection);
            await monitor.OpenAsync(timeout.Token);
            while (true)
            {
                await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = @pid AND wait_event_type = 'Lock')", monitor);
                command.Parameters.AddWithValue("pid", logoutPid);
                if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!) break;
                await Task.Delay(10, timeout.Token);
            }
            commit.Release.TrySetResult();
            var replacement = await refreshing;
            await loggingOut;
            Assert.NotNull(replacement);
            await fixture.InDb(async db =>
            {
                var hash = SecureTokens.Hash(replacement.RefreshToken);
                Assert.NotNull((await db.RefreshSessions.SingleAsync(x => x.TokenHash == hash)).RevokedAt);
            });
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh",
                new { refreshToken = replacement.RefreshToken })).StatusCode);
        }
        finally
        {
            commit.Release.TrySetResult();
            try { await refreshing; if (loggingOut != null) await loggingOut; }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        }
    }

    [PostgresFact]
    public async Task Concurrent_refresh_reuse_revokes_the_winning_replacement()
    {
        using var client = fixture.Client();
        var registration = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = Guid.NewGuid().ToString("N") + "@example.com", password = "Audit-test-Password1!", displayName = "Concurrent refresh"
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var initial = (await registration.Content.ReadFromJsonAsync<AuthTokenResponseDto>())!;
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = initial.RefreshToken })));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
        var winning = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var tokens = (await winning.Content.ReadFromJsonAsync<AuthTokenResponseDto>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = tokens.RefreshToken })).StatusCode);
    }

    private sealed class PauseCommitInterceptor : DbTransactionInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
