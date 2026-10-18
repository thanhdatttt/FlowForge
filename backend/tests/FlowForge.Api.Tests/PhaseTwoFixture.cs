using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net;
using Microsoft.AspNetCore.Builder;
using System.Security.Claims;
using System.Text;
using FlowForge.Application.Email;
using Microsoft.AspNetCore.DataProtection;
using FlowForge.Domain.Identity;
using FlowForge.Domain.Workspaces;
using FlowForge.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLOWFORGE_TEST_CONNECTION")))
            Skip = "Set FLOWFORGE_TEST_CONNECTION to an isolated PostgreSQL server to run integration tests.";
    }
}

public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLOWFORGE_TEST_CONNECTION")))
            Skip = "Set FLOWFORGE_TEST_CONNECTION to an isolated PostgreSQL server to run integration tests.";
    }
}

public sealed class TestEmailSender : IEmailSender
{
    public bool IsConfigured { get; set; } = true;
    public bool FailDelivery { get; set; }
    public ConcurrentDictionary<string, string> Codes { get; } = new();
    public ConcurrentQueue<(string Recipient, string Subject, string Body)> Messages { get; } = new();
    public Func<string, CancellationToken, Task>? BeforeSendAsync { get; set; }
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken ct)
    {
        if (BeforeSendAsync != null) await BeforeSendAsync(recipient, ct);
        if (FailDelivery) throw new System.Net.Mail.SmtpException("Test-only delivery failure");
        Messages.Enqueue((recipient, subject, body));
    }
    public Task SendVerificationAsync(string recipient, string code, int lifetimeMinutes, CancellationToken ct)
    {
        if (FailDelivery) throw new System.Net.Mail.SmtpException("Test-only delivery failure");
        Codes[recipient] = code;
        return Task.CompletedTask;
    }
}

public class PhaseTwoFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "phase-two-test-signing-key-at-least-32-bytes";
    public TestEmailSender Sender { get; } = new();
    private static int clientSequence;
    private string? connection;
    private readonly string database = "flowforge_tests_" + Guid.NewGuid().ToString("N");
    public string AttachmentPath { get; } = Path.Combine(Path.GetTempPath(), "flowforge-test-files-" + Guid.NewGuid().ToString("N"));
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:FlowForge", connection!);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Database:AutoMigrate", "false");
        builder.UseSetting("Notifications:Enabled", "false");
        builder.UseSetting("Workflows:Enabled", "false");
        builder.UseSetting("Attachments:StoragePath", AttachmentPath);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, TestClientAddressFilter>();
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Sender);
        });
    }
    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("FLOWFORGE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var settings = new NpgsqlConnectionStringBuilder(configured);
        await using var admin = new NpgsqlConnection(configured);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
        await command.ExecuteNonQueryAsync();
        settings.Database = database;
        connection = settings.ConnectionString;
        await InDb(async db => await db.Database.MigrateAsync());
    }
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (connection == null) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(Environment.GetEnvironmentVariable("FLOWFORGE_TEST_CONNECTION"));
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
        if (Directory.Exists(AttachmentPath)) Directory.Delete(AttachmentPath, recursive: true);
    }
    public async Task InDb(Func<FlowForgeDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<FlowForgeDbContext>());
    }
    public async Task<User> UserAsync(bool verified = true)
    {
        var user = new User { Email = Guid.NewGuid().ToString("N") + "@example.com", NormalizedEmail = "",
            DisplayName = "Phase Two User", PasswordHash = "test-only-not-a-password-hash",
            EmailVerifiedAt = verified ? DateTimeOffset.UtcNow : null };
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        await InDb(async db => { db.Users.Add(user); await db.SaveChangesAsync(); });
        return user;
    }
    public HttpClient Client(User? user = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Client-IP", $"2001:db8::{Interlocked.Increment(ref clientSequence):x}");
        if (user != null)
        {
            var jwt = new JwtSecurityToken("flowforge", "flowforge-api",
                [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString())],
                expires: DateTime.UtcNow.AddMinutes(15), signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        }
        return client;
    }
    public async Task<Workspace> WorkspaceAsync(User owner, params (User User, WorkspaceRole Role)[] members)
    {
        var workspace = new Workspace { Name = "Test Workspace", CreatedByUserId = owner.Id };
        await InDb(async db =>
        {
            db.Workspaces.Add(workspace);
            db.WorkspaceMemberships.Add(new() { WorkspaceId = workspace.Id, UserId = owner.Id, Role = WorkspaceRole.Owner });
            foreach (var (user, role) in members)
                db.WorkspaceMemberships.Add(new() { WorkspaceId = workspace.Id, UserId = user.Id, Role = role });
            await db.SaveChangesAsync();
        });
        return workspace;
    }
}

// Give each test client its own address so unrelated test cases do not share
// an IP rate-limit budget. This middleware exists only in the test host.
public sealed class TestClientAddressFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, handler) =>
        {
            if (IPAddress.TryParse(context.Request.Headers["X-Test-Client-IP"].ToString(), out var address))
                context.Connection.RemoteIpAddress = address;
            return handler(context);
        });
        next(app);
    };
}
