using System.Net.Mail;
using System.Net.Http.Json;
using FlowForge.Api.Contracts.Workspaces;
using FlowForge.Domain.WorkItems;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseFourReliabilityTests(PhaseFourFixture fixture) : IClassFixture<PhaseFourFixture>
{
    [PostgresFact]
    public async Task Realtime_failure_retries_after_commit_without_duplicate_notifications_or_email()
    {
        var journey = new PhaseFourIntegrationTests(fixture);
        var (owner, member, project, path) = await journey.SetupAsync();
        using var client = fixture.Client(owner);
        var item = await PhaseFourIntegrationTests.CreateItemAsync(client, path, member.Id);
        fixture.Realtime.FailuresRemaining = 1;
        await journey.DrainOutboxAsync();
        await fixture.InDb(async db =>
        {
            var message = await db.OutboxMessages.SingleAsync(x => db.DomainEvents.Any(e => e.Id == x.EventId && e.EntityId == item.Id));
            Assert.Null(message.ProcessedAt);
            Assert.Null(message.FailedAt);
            Assert.Equal(1, message.AttemptCount);
            Assert.Equal("Transient delivery failure.", message.SafeError);
            Assert.Single(await db.Notifications.Where(x => x.ProjectId == project.Id).ToListAsync());
        });
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        await journey.DrainOutboxAsync();
        await fixture.InDb(async db =>
        {
            Assert.Single(await db.Notifications.Where(x => x.ProjectId == project.Id).ToListAsync());
            Assert.Single(await db.EmailDeliveries.Where(x => db.Notifications.Any(n => n.Id == x.NotificationId && n.ProjectId == project.Id)).ToListAsync());
            var message = await db.OutboxMessages.SingleAsync(x => db.DomainEvents.Any(e => e.Id == x.EventId && e.EntityId == item.Id));
            Assert.NotNull(message.ProcessedAt);
            Assert.Equal(2, message.AttemptCount);
            Assert.Null(message.SafeError);
        });
    }

    [PostgresFact]
    public async Task Concurrent_dispatchers_and_recovered_claims_process_each_notification_once()
    {
        var journey = new PhaseFourIntegrationTests(fixture);
        var (owner, member, project, path) = await journey.SetupAsync();
        using var client = fixture.Client(owner);
        for (var i = 0; i < 6; i++) await PhaseFourIntegrationTests.CreateItemAsync(client, path, member.Id);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => journey.DrainOutboxAsync()));
        await fixture.InDb(async db => Assert.Equal(6, await db.Notifications.CountAsync(x => x.ProjectId == project.Id)));
        var recovered = await PhaseFourIntegrationTests.CreateItemAsync(client, path, member.Id);
        await fixture.InDb(async db =>
        {
            var message = await db.OutboxMessages.SingleAsync(x => db.DomainEvents.Any(e => e.Id == x.EventId && e.EntityId == recovered.Id));
            message.LockId = Guid.NewGuid();
            message.AttemptCount = 1;
            message.AvailableAt = fixture.Clock.GetUtcNow().AddSeconds(-1);
            await db.SaveChangesAsync();
        });
        await journey.DrainOutboxAsync();
        await fixture.InDb(async db => Assert.Equal(7, await db.Notifications.CountAsync(x => x.ProjectId == project.Id)));
    }

    [PostgresFact]
    public async Task Malformed_events_and_exhausted_crash_claims_fail_without_poisoning_other_messages()
    {
        var journey = new PhaseFourIntegrationTests(fixture);
        var (owner, member, project, path) = await journey.SetupAsync();
        using var client = fixture.Client(owner);
        var item = await PhaseFourIntegrationTests.CreateItemAsync(client, path, member.Id);
        Guid badId = default, exhaustedId = default;
        await fixture.InDb(async db =>
        {
            var bad = new DomainEvent { WorkspaceId = project.WorkspaceId, ProjectId = project.Id, EntityId = item.Id,
                EventType = "CommentAdded", Payload = "{}", CorrelationId = "test" };
            var exhausted = new DomainEvent { WorkspaceId = project.WorkspaceId, ProjectId = project.Id, EntityId = item.Id,
                EventType = "WorkItemUpdated", Payload = "{}", CorrelationId = "test" };
            db.DomainEvents.AddRange(bad, exhausted);
            var first = new OutboxMessage { EventId = bad.Id, MessageType = "DomainEvent", AvailableAt = fixture.Clock.GetUtcNow() };
            var second = new OutboxMessage { EventId = exhausted.Id, MessageType = "DomainEvent", AttemptCount = 5,
                LockId = Guid.NewGuid(), AvailableAt = fixture.Clock.GetUtcNow().AddSeconds(-1) };
            badId = first.Id; exhaustedId = second.Id;
            db.OutboxMessages.AddRange(first, second);
            await db.SaveChangesAsync();
        });
        await journey.DrainOutboxAsync();
        await fixture.InDb(async db =>
        {
            var bad = await db.OutboxMessages.SingleAsync(x => x.Id == badId);
            Assert.NotNull(bad.FailedAt);
            Assert.Equal("Permanent delivery failure.", bad.SafeError);
            Assert.NotNull((await db.OutboxMessages.SingleAsync(x => x.Id == exhaustedId)).FailedAt);
            Assert.Single(await db.Notifications.Where(x => x.ProjectId == project.Id).ToListAsync());
        });
    }

    [PostgresFact]
    public async Task Email_transient_failure_backs_off_and_stops_at_attempt_limit()
    {
        var owner = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner);
        using var client = fixture.Client(owner);
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/invitations",
            new CreateInvitationRequestDto(Guid.NewGuid().ToString("N") + "@example.com", "Developer"));
        var invitation = (await response.Content.ReadFromJsonAsync<CreatedInvitationResponseDto>())!;
        fixture.Sender.FailDelivery = true;
        try
        {
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                await fixture.InScope(async services =>
                {
                    while (await services.GetRequiredService<EmailDeliveryProcessor>().SendNextAsync(default)) { }
                });
                await fixture.InDb(async db =>
                {
                    var delivery = await db.EmailDeliveries.SingleAsync(x => x.InvitationId == invitation.Invitation.Id);
                    Assert.Equal(attempt, delivery.AttemptCount);
                    Assert.Equal(attempt == 5 ? "Failed" : "Pending", delivery.Status);
                    Assert.Equal("Transient delivery failure.", delivery.SafeError);
                    if (attempt == 5) Assert.Empty(delivery.ProtectedBody);
                });
                fixture.Clock.Advance(TimeSpan.FromMinutes(16));
            }
        }
        finally { fixture.Sender.FailDelivery = false; }
    }

    [PostgresFact]
    public async Task Disabled_smtp_keeps_in_app_notifications_available_without_creating_email_jobs()
    {
        var journey = new PhaseFourIntegrationTests(fixture);
        var (owner, member, project, path) = await journey.SetupAsync();
        fixture.Sender.IsConfigured = false;
        try
        {
            using var client = fixture.Client(owner);
            await PhaseFourIntegrationTests.CreateItemAsync(client, path, member.Id);
            await journey.DrainOutboxAsync();
            await fixture.InDb(async db =>
            {
                Assert.Single(await db.Notifications.Where(x => x.ProjectId == project.Id).ToListAsync());
                Assert.Empty(await db.EmailDeliveries.Where(x => db.Notifications.Any(n => n.Id == x.NotificationId && n.ProjectId == project.Id)).ToListAsync());
            });
        }
        finally { fixture.Sender.IsConfigured = true; }
    }

    [PostgresFact]
    public async Task Rolled_back_events_are_never_published_or_notified()
    {
        var journey = new PhaseFourIntegrationTests(fixture);
        var (owner, member, project, path) = await journey.SetupAsync();
        using var client = fixture.Client(owner);
        var item = await PhaseFourIntegrationTests.CreateItemAsync(client, path, member.Id);
        await journey.DrainOutboxAsync();
        var eventId = Guid.NewGuid();
        await fixture.InDb(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.DomainEvents.Add(new DomainEvent { Id = eventId, WorkspaceId = project.WorkspaceId, ProjectId = project.Id,
                EntityId = item.Id, EventType = "WorkItemUpdated", Payload = "{}", CorrelationId = "rollback" });
            db.OutboxMessages.Add(new OutboxMessage { EventId = eventId, MessageType = "DomainEvent", AvailableAt = fixture.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        });
        await journey.DrainOutboxAsync();
        Assert.DoesNotContain(fixture.Realtime.Updates, x => x.Update.EventId == eventId);
        await fixture.InDb(async db =>
        {
            Assert.False(await db.DomainEvents.AnyAsync(x => x.Id == eventId));
            Assert.Single(await db.Notifications.Where(x => x.ProjectId == project.Id).ToListAsync());
        });
    }

    [PostgresFact]
    public async Task Hosted_worker_runs_registered_overdue_dispatch_and_email_services()
    {
        var journey = new PhaseFourIntegrationTests(fixture);
        var (owner, member, project, path) = await journey.SetupAsync();
        using var client = fixture.Client(owner);
        await PhaseFourIntegrationTests.CreateItemAsync(client, path, member.Id,
            DateOnly.FromDateTime(fixture.Clock.GetUtcNow().UtcDateTime).AddDays(-1));
        using var worker = new FlowForge.Api.NotificationWorker(fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new NotificationJobOptions { PollSeconds = 1 }), fixture.Clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FlowForge.Api.NotificationWorker>.Instance);
        await worker.StartAsync(default);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                var complete = false;
                await fixture.InDb(async db => complete = await db.Notifications.AnyAsync(x => x.ProjectId == project.Id && x.Type == "Overdue") &&
                    await db.EmailDeliveries.AnyAsync(x => x.Status == "Sent" && db.Notifications.Any(n => n.Id == x.NotificationId && n.ProjectId == project.Id)));
                if (complete) break;
                await Task.Delay(50, timeout.Token);
            }
        }
        finally { await worker.StopAsync(default); }
    }

    [PostgresFact]
    public async Task Slow_email_does_not_block_workspace_writes_or_realtime_dispatch_and_contains_no_private_data()
    {
        var journey = new PhaseFourIntegrationTests(fixture);
        var (owner, member, project, path) = await journey.SetupAsync();
        using var client = fixture.Client(owner);
        var first = await PhaseFourIntegrationTests.CreateItemAsync(client, path, member.Id);
        await journey.DrainOutboxAsync();
        // Simulate a body queued by the earlier implementation. Even this email must be
        // rendered safely if access is revoked while SMTP is already in flight.
        await fixture.InScope(async services =>
        {
            var db = services.GetRequiredService<FlowForgeDbContext>();
            var email = await db.EmailDeliveries.SingleAsync(x => db.Notifications.Any(n => n.Id == x.NotificationId && n.WorkItemId == first.Id));
            email.ProtectedBody = services.GetRequiredService<FlowForge.Application.Notifications.IEmailContentProtector>()
                .Protect("private-project-details " + first.Id);
            await db.SaveChangesAsync();
        });
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Sender.BeforeSendAsync = async (recipient, ct) =>
        {
            if (recipient != member.Email) return;
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
        };
        using var worker = CreateWorker();
        await worker.StartAsync(default);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var second = await PhaseFourIntegrationTests.CreateItemAsync(client, path, member.Id).WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForAsync(() => Task.FromResult(fixture.Realtime.Updates.Any(x => x.Update.EntityId == second.Id)));
            Assert.Equal(System.Net.HttpStatusCode.NoContent,
                (await client.DeleteAsync(path + $"/members/{member.Id}").WaitAsync(TimeSpan.FromSeconds(5))).StatusCode);
            release.TrySetResult();
            await WaitForAsync(() => Task.FromResult(fixture.Sender.Messages.Any(x => x.Recipient == member.Email)));
            var sent = Assert.Single(fixture.Sender.Messages, x => x.Recipient == member.Email);
            Assert.Equal(NotificationQueue.NotificationEmailBody, sent.Body);
            Assert.DoesNotContain(first.Id.ToString(), sent.Body);
            Assert.DoesNotContain(project.Id.ToString(), sent.Body);
            Assert.DoesNotContain("private-project-details", sent.Body);
        }
        finally
        {
            release.TrySetResult();
            fixture.Sender.BeforeSendAsync = null;
            await worker.StopAsync(default);
        }
    }

    [PostgresFact]
    public async Task Failing_overdue_scan_does_not_block_outbox_or_email_jobs()
    {
        var journey = new PhaseFourIntegrationTests(fixture);
        var (owner, member, project, path) = await journey.SetupAsync();
        using var client = fixture.Client(owner);
        var item = await PhaseFourIntegrationTests.CreateItemAsync(client, path, member.Id);
        var previousFailures = fixture.OverdueFailure.FailureCount;
        fixture.OverdueFailure.Enabled = true;
        using var worker = CreateWorker();
        await worker.StartAsync(default);
        try
        {
            await WaitForAsync(async () =>
            {
                var sent = false;
                await fixture.InDb(async db => sent = await db.EmailDeliveries.AnyAsync(e => e.Status == "Sent" &&
                    db.Notifications.Any(n => n.Id == e.NotificationId && n.WorkItemId == item.Id)));
                return sent && fixture.OverdueFailure.FailureCount > previousFailures;
            });
            Assert.Contains(fixture.Realtime.Updates, x => x.Update.EntityId == item.Id);
            Assert.Contains(fixture.Sender.Messages, x => x.Recipient == member.Email);
        }
        finally
        {
            fixture.OverdueFailure.Enabled = false;
            await worker.StopAsync(default);
        }
    }

    private FlowForge.Api.NotificationWorker CreateWorker() => new(fixture.Services.GetRequiredService<IServiceScopeFactory>(),
        Microsoft.Extensions.Options.Options.Create(new NotificationJobOptions { PollSeconds = 1 }), fixture.Clock,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<FlowForge.Api.NotificationWorker>.Instance);

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!await condition()) await Task.Delay(50, timeout.Token);
    }

    [Theory]
    [InlineData(SmtpStatusCode.MailboxBusy, true)]
    [InlineData(SmtpStatusCode.MailboxUnavailable, false)]
    [InlineData(SmtpStatusCode.GeneralFailure, true)]
    public void Retry_classification_does_not_retry_permanent_smtp_failures(SmtpStatusCode status, bool retry) =>
        Assert.Equal(retry, NotificationRetry.IsTransient(new SmtpException(status, "secret")));
}
