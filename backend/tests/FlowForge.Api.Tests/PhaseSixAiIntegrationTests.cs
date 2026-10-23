using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowForge.Api.Contracts.Ai;
using FlowForge.Api.Contracts.Dashboard;
using FlowForge.Api.Contracts.Projects;
using FlowForge.Api.Contracts.WorkItems;
using FlowForge.Application.Ai;
using FlowForge.Domain.Identity;
using FlowForge.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseSixAiIntegrationTests(PhaseSixAiFixture fixture) : IClassFixture<PhaseSixAiFixture>
{
    [PostgresFact]
    public async Task Preview_is_read_only_and_explicit_edited_acceptance_creates_tasks_dependencies_audit_and_outbox()
    {
        var s = await SetupAsync();
        using var client = fixture.Client(s.Owner);
        var preview = await PreviewAsync(client, s);
        Assert.True(preview.IsAiGenerated); Assert.Equal(2, preview.Tasks.Count);
        Assert.Equal(fixture.Clock.Now.AddMinutes(15), preview.ExpiresAt);
        Assert.Equal(s.Project.Id, fixture.Provider.TaskContext!.ProjectId);
        Assert.Equal("Implement feature", fixture.Provider.TaskContext.FeatureDescription);
        await fixture.InDb(async db =>
        {
            Assert.Empty(await db.WorkItems.Where(x => x.ProjectId == s.Project.Id).ToListAsync());
            Assert.Empty(await db.DomainEvents.Where(x => x.ProjectId == s.Project.Id).ToListAsync());
            Assert.False(await db.ActivityRecords.AnyAsync(x => x.ProjectId == s.Project.Id && x.Action.StartsWith("ai.")));
        });
        var edited = preview.Tasks.Select(t => t.Id == "feature" ? t with { Title = "Edited feature", Priority = "Critical" } : t).ToArray();
        using var declined = await client.PostAsJsonAsync(s.AiPath + "/tasks/accept", new AiTaskAcceptanceRequestDto(preview.PreviewToken, false, edited));
        Assert.Equal(HttpStatusCode.BadRequest, declined.StatusCode);
        var accepted = await AcceptAsync(client, s, preview, edited);
        Assert.Equal(2, accepted.Tasks.Count);
        await fixture.InDb(async db =>
        {
            var items = await db.WorkItems.Where(x => x.ProjectId == s.Project.Id).OrderBy(x => x.ItemNumber).ToListAsync();
            Assert.Equal(2, items.Count); Assert.All(items, x => { Assert.Equal("Task", x.Type); Assert.Null(x.AssigneeMembershipId); });
            Assert.Equal("Edited feature", items[1].Title); Assert.Equal("Critical", items[1].Priority);
            var dependency = Assert.Single(await db.WorkItemDependencies.Where(x => x.ProjectId == s.Project.Id).ToListAsync());
            Assert.Equal(items[1].Id, dependency.WorkItemId); Assert.Equal(items[0].Id, dependency.DependsOnItemId);
            Assert.Equal(2, await db.DomainEvents.CountAsync(x => x.ProjectId == s.Project.Id && x.EventType == "WorkItemCreated"));
            Assert.Equal(2, await db.OutboxMessages.CountAsync(x => db.DomainEvents.Any(e => e.Id == x.EventId && e.ProjectId == s.Project.Id)));
            Assert.Single(await db.ActivityRecords.Where(x => x.EntityId == accepted.BatchId && x.Action == "ai.tasks.accepted").ToListAsync());
        });
        var blocked = await client.GetFromJsonAsync<WorkItemDto>(s.ProjectPath + "/work-items/" + accepted.Tasks[1].Id);
        Assert.True(blocked!.IsBlocked);
    }

    [PostgresFact]
    public async Task Concurrent_replays_create_one_batch_and_modified_replays_conflict()
    {
        var s = await SetupAsync();
        using var first = fixture.Client(s.Owner);
        using var second = fixture.Client(s.Owner);
        var preview = await PreviewAsync(first, s);
        var responses = await Task.WhenAll(AcceptAsync(first, s, preview), AcceptAsync(second, s, preview));
        Assert.Equal(responses[0].BatchId, responses[1].BatchId);
        Assert.Equal(responses[0].Tasks.Select(x => x.Id), responses[1].Tasks.Select(x => x.Id));
        var reordered = await AcceptAsync(first, s, preview, preview.Tasks.Reverse().ToArray());
        Assert.Equal(responses[0].Tasks.Select(x => x.Id), reordered.Tasks.Select(x => x.Id));
        using var conflict = await first.PostAsJsonAsync(s.AiPath + "/tasks/accept",
            new AiTaskAcceptanceRequestDto(preview.PreviewToken, true, preview.Tasks.Select(x => x with { Priority = "Low" }).ToArray()));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await fixture.InDb(async db =>
        {
            Assert.Equal(2, await db.WorkItems.CountAsync(x => x.ProjectId == s.Project.Id));
            Assert.Equal(3, (await db.Projects.SingleAsync(x => x.Id == s.Project.Id)).NextItemNumber);
        });
    }

    [PostgresFact]
    public async Task Concurrent_different_previews_allocate_distinct_item_numbers()
    {
        var s = await SetupAsync();
        using var first = fixture.Client(s.Owner); using var second = fixture.Client(s.Owner);
        var a = await PreviewAsync(first, s); var b = await PreviewAsync(second, s);
        var results = await Task.WhenAll(AcceptAsync(first, s, a), AcceptAsync(second, s, b));
        Assert.Equal(4, results.SelectMany(x => x.Tasks).Select(x => x.Key).Distinct().Count());
        await fixture.InDb(async db => Assert.Equal(new long[] { 1, 2, 3, 4 },
            await db.WorkItems.Where(x => x.ProjectId == s.Project.Id).OrderBy(x => x.ItemNumber).Select(x => x.ItemNumber).ToArrayAsync()));
    }

    [PostgresFact]
    public async Task Selection_rejects_dangling_dependencies_and_foreign_ids_but_accepts_valid_subset()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var preview = await PreviewAsync(client, s);
        foreach (var tasks in new[]
        {
            new[] { preview.Tasks[1] },
            new[] { preview.Tasks[0] with { Id = "invented" } },
            new[] { preview.Tasks[0] with { DependsOn = ["external-task"] } },
            new[] { preview.Tasks[0] with { DependsOn = ["feature"] }, preview.Tasks[1] }
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(s.AiPath + "/tasks/accept",
                new AiTaskAcceptanceRequestDto(preview.PreviewToken, true, tasks))).StatusCode);
        var accepted = await AcceptAsync(client, s, preview, [preview.Tasks[0]]);
        Assert.Single(accepted.Tasks);
    }

    [PostgresFact]
    public async Task Preview_tokens_reject_tampering_expiry_and_different_users_or_projects()
    {
        var s = await SetupAsync(); using var owner = fixture.Client(s.Owner); using var member = fixture.Client(s.Member);
        var preview = await PreviewAsync(owner, s);
        var request = new AiTaskAcceptanceRequestDto(preview.PreviewToken, true, preview.Tasks);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsJsonAsync(s.AiPath + "/tasks/accept", request)).StatusCode);
        var other = await SetupAsync(); using var otherClient = fixture.Client(other.Owner);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PostAsJsonAsync(other.AiPath + "/tasks/accept", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(s.AiPath + "/tasks/accept",
            request with { PreviewToken = preview.PreviewToken[..(preview.PreviewToken.Length / 2)] + "tampered" })).StatusCode);
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(16);
        Assert.Equal(HttpStatusCode.Gone, (await owner.PostAsJsonAsync(s.AiPath + "/tasks/accept", request)).StatusCode);
    }

    [PostgresFact]
    public async Task Deferred_commit_failure_rolls_back_tasks_dependencies_counter_receipt_activity_and_events()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var preview = await PreviewAsync(client, s);
        await fixture.InDb(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION reject_ai_test_commit() RETURNS trigger AS $$
            BEGIN
                IF NEW.title = 'Reject AI commit' THEN RAISE EXCEPTION 'test commit failure'; END IF;
                RETURN NEW;
            END; $$ LANGUAGE plpgsql;
            CREATE CONSTRAINT TRIGGER reject_ai_test_commit AFTER INSERT ON work_items
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_ai_test_commit();
            """));
        var tasks = preview.Tasks.Select(x => x.Id == "feature" ? x with { Title = "Reject AI commit" } : x).ToArray();
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync(s.AiPath + "/tasks/accept",
            new AiTaskAcceptanceRequestDto(preview.PreviewToken, true, tasks))).StatusCode);
        await fixture.InDb(async db =>
        {
            Assert.Empty(await db.WorkItems.Where(x => x.ProjectId == s.Project.Id).ToListAsync());
            Assert.Empty(await db.WorkItemDependencies.Where(x => x.ProjectId == s.Project.Id).ToListAsync());
            Assert.Empty(await db.DomainEvents.Where(x => x.ProjectId == s.Project.Id).ToListAsync());
            Assert.False(await db.ActivityRecords.AnyAsync(x => x.ProjectId == s.Project.Id && (x.Action == "ai.tasks.accepted" || x.EntityType == "WorkItem")));
            Assert.Equal(1, (await db.Projects.SingleAsync(x => x.Id == s.Project.Id)).NextItemNumber);
        });
        Assert.Equal(2, (await AcceptAsync(client, s, preview)).Tasks.Count);
    }

    [PostgresTheory]
    [InlineData(WorkspaceRole.Admin, false, HttpStatusCode.OK)]
    [InlineData(WorkspaceRole.Manager, true, HttpStatusCode.OK)]
    [InlineData(WorkspaceRole.Developer, true, HttpStatusCode.OK)]
    [InlineData(WorkspaceRole.Viewer, true, HttpStatusCode.Forbidden)]
    [InlineData(WorkspaceRole.Developer, false, HttpStatusCode.NotFound)]
    [InlineData(WorkspaceRole.Manager, false, HttpStatusCode.NotFound)]
    public async Task Proposal_and_acceptance_enforce_current_resource_write_permissions(WorkspaceRole role, bool assigned, HttpStatusCode expected)
    {
        var s = await SetupAsync(role, assigned); using var member = fixture.Client(s.Member);
        Assert.Equal(expected, (await member.PostAsJsonAsync(s.AiPath + "/tasks/propose", new AiTaskProposalRequestDto("Feature"))).StatusCode);
        if (expected != HttpStatusCode.OK)
        {
            Assert.Equal(expected, (await member.PostAsJsonAsync(s.AiPath + "/tasks/accept",
                new AiTaskAcceptanceRequestDto("invalid", true, TestAiProvider.Proposals))).StatusCode);
            Assert.Equal(0, fixture.Provider.Calls);
        }
        else
        {
            var preview = await PreviewAsync(member, s);
            Assert.Equal(2, (await AcceptAsync(member, s, preview)).Tasks.Count);
        }
    }

    [PostgresFact]
    public async Task Anonymous_outsiders_mismatched_scopes_and_removed_users_are_rejected_before_provider_calls()
    {
        var s = await SetupAsync();
        using var anonymous = fixture.Client(); using var outsider = fixture.Client(await fixture.UserAsync());
        using var member = fixture.Client(s.Member);
        foreach (var suffix in new[] { "/tasks/propose", "/summary", "/tasks/accept" })
        {
            var body = new { featureDescription = "Feature", previewToken = "invalid", accepted = true, tasks = TestAiProvider.Proposals };
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(s.AiPath + suffix, body)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync(s.AiPath + suffix, body)).StatusCode);
            var mismatched = $"/api/v1/workspaces/{s.Workspace.Id}/projects/{Guid.NewGuid()}/ai";
            Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsJsonAsync(mismatched + suffix, body)).StatusCode);
        }
        await fixture.InDb(async db =>
        {
            (await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == s.Workspace.Id && x.UserId == s.Member.Id)).RemovedAt = fixture.Clock.Now;
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsJsonAsync(s.AiPath + "/summary", new { })).StatusCode);
        Assert.Equal(0, fixture.Provider.Calls);
    }

    [PostgresFact]
    public async Task Revocation_during_generation_prevents_preview_and_acceptance_rechecks_permissions()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Member);
        var preview = await PreviewAsync(client, s);
        fixture.Provider.Tasks = async _ =>
        {
            await fixture.InDb(async db =>
            {
                (await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == s.Workspace.Id && x.UserId == s.Member.Id)).Role = WorkspaceRole.Viewer;
                await db.SaveChangesAsync();
            });
            return TestAiProvider.ValidTasks;
        };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(s.AiPath + "/tasks/propose", new AiTaskProposalRequestDto("Feature"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(s.AiPath + "/tasks/accept",
            new AiTaskAcceptanceRequestDto(preview.PreviewToken, true, preview.Tasks))).StatusCode);
    }

    [PostgresFact]
    public async Task Summary_facts_match_dashboard_and_context_is_bounded_scoped_and_separate_from_interpretation()
    {
        var s = await SetupAsync(WorkspaceRole.Viewer); using var owner = fixture.Client(s.Owner); using var viewer = fixture.Client(s.Member);
        await ItemAsync(owner, s, "Done item", "Done");
        await ItemAsync(owner, s, "Blocker", "Blocked");
        await ItemAsync(owner, s, "Overdue", "Todo", fixture.Clock.Now.UtcDateTime.Date.AddDays(-1));
        var archived = await ItemAsync(owner, s, "Archived", "Todo");
        await owner.PostAsync(s.ProjectPath + $"/work-items/{archived.Id}/archive", null);
        using var siblingResponse = await owner.PostAsJsonAsync($"/api/v1/workspaces/{s.Workspace.Id}/projects",
            new CreateProjectRequestDto("Private sibling", "SIB"));
        siblingResponse.EnsureSuccessStatusCode();
        var sibling = (await siblingResponse.Content.ReadFromJsonAsync<ProjectResponseDto>())!;
        await ItemAsync(owner, s with { Project = sibling }, "PRIVATE SIBLING PROJECT", "Todo");
        var other = await SetupAsync(); using var otherClient = fixture.Client(other.Owner);
        await ItemAsync(otherClient, other, "PRIVATE OTHER PROJECT", "Todo");
        fixture.Provider.Summary = _ => Task.FromResult(TestAiProvider.ValidSummary);
        var response = await viewer.PostAsJsonAsync(s.AiPath + "/summary", new { }); response.EnsureSuccessStatusCode();
        var summary = (await response.Content.ReadFromJsonAsync<AiProjectSummaryDto>())!;
        var dashboard = (await owner.GetFromJsonAsync<ProjectDashboardDto>(s.ProjectPath + "/dashboard"))!;
        Assert.Equal("Generated", summary.Status); Assert.True(summary.IsAiGenerated); Assert.NotNull(summary.Narrative);
        Assert.Equal(dashboard.TotalItems, summary.Facts.TotalItems);
        Assert.Equal(dashboard.CompletedItems, summary.Facts.CompletedItems);
        Assert.Equal(dashboard.CompletionPercentage, summary.Facts.CompletionPercentage);
        Assert.Equal(dashboard.OverdueItems, summary.Facts.OverdueItems);
        Assert.Equal(1, summary.Facts.BlockedItems);
        Assert.True(summary.ContextTruncated); Assert.Equal(2, summary.ContextItemCount);
        var context = fixture.Provider.SummaryContext!;
        Assert.Equal(s.Project.Id, context.ProjectId); Assert.Equal(s.Workspace.Id, context.WorkspaceId);
        Assert.DoesNotContain(context.Items, x => x.Title is "PRIVATE OTHER PROJECT" or "PRIVATE SIBLING PROJECT" or "Archived");
        Assert.DoesNotContain("PRIVATE OTHER PROJECT", await response.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task Empty_project_returns_insufficient_data_without_a_provider_and_regular_features_still_work()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        fixture.Provider.IsConfigured = false;
        var response = await client.PostAsJsonAsync(s.AiPath + "/summary", new { }); response.EnsureSuccessStatusCode();
        var summary = (await response.Content.ReadFromJsonAsync<AiProjectSummaryDto>())!;
        Assert.Equal("InsufficientData", summary.Status); Assert.Null(summary.Narrative); Assert.False(summary.IsAiGenerated);
        Assert.Equal(0, fixture.Provider.Calls);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(s.AiPath + "/tasks/propose", new AiTaskProposalRequestDto("Feature"))).StatusCode);
        await ItemAsync(client, s, "Normal task", "Todo");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync(s.AiPath + "/summary", new { })).StatusCode);
        Assert.Equal(1, (await client.GetFromJsonAsync<ProjectDashboardDto>(s.ProjectPath + "/dashboard"))!.TotalItems);
    }

    [PostgresFact]
    public async Task Context_limits_description_length_and_derives_dependency_blockers_on_the_server()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var first = await ItemAsync(client, s, "Prerequisite", "Todo");
        var second = await ItemAsync(client, s, "Dependent", "InProgress");
        await fixture.InDb(async db =>
        {
            (await db.WorkItems.SingleAsync(x => x.Id == second.Id)).Description = new string('x', 3000);
            db.WorkItemDependencies.Add(new() { ProjectId = s.Project.Id, WorkItemId = second.Id, DependsOnItemId = first.Id });
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(s.AiPath + "/summary", new { })).StatusCode);
        var context = fixture.Provider.SummaryContext!;
        Assert.False(context.ContextTruncated); Assert.Equal(1, context.Facts.BlockedItems);
        var item = Assert.Single(context.Items, x => x.Id == second.Id);
        Assert.True(item.IsBlocked); Assert.Equal(2000, item.Description!.Length);
    }

    [PostgresFact]
    public async Task Archived_projects_allow_summaries_but_not_proposals_or_acceptance()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var preview = await PreviewAsync(client, s); await ItemAsync(client, s, "Work", "Todo");
        await client.PostAsync(s.ProjectPath + "/archive", null);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(s.AiPath + "/summary", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(s.AiPath + "/tasks/propose", new AiTaskProposalRequestDto("Feature"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(s.AiPath + "/tasks/accept",
            new AiTaskAcceptanceRequestDto(preview.PreviewToken, true, preview.Tasks))).StatusCode);
    }

    [PostgresFact]
    public async Task Summary_rechecks_access_after_provider_completes()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Member); using var owner = fixture.Client(s.Owner);
        await ItemAsync(owner, s, "Private", "Todo");
        fixture.Provider.Summary = async _ =>
        {
            await fixture.InDb(async db =>
            {
                (await db.ProjectMemberships.SingleAsync(x => x.ProjectId == s.Project.Id)).RemovedAt = fixture.Clock.Now;
                await db.SaveChangesAsync();
            });
            return TestAiProvider.ValidSummary;
        };
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(s.AiPath + "/summary", new { })).StatusCode);
    }

    [PostgresTheory]
    [InlineData("tasks/propose", AiProviderFailure.Unavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData("tasks/propose", AiProviderFailure.RateLimited, HttpStatusCode.TooManyRequests)]
    [InlineData("tasks/propose", AiProviderFailure.Timeout, HttpStatusCode.GatewayTimeout)]
    [InlineData("tasks/propose", AiProviderFailure.InvalidResponse, HttpStatusCode.BadGateway)]
    [InlineData("summary", AiProviderFailure.Unavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData("summary", AiProviderFailure.RateLimited, HttpStatusCode.TooManyRequests)]
    [InlineData("summary", AiProviderFailure.Timeout, HttpStatusCode.GatewayTimeout)]
    [InlineData("summary", AiProviderFailure.InvalidResponse, HttpStatusCode.BadGateway)]
    public async Task Provider_failures_return_safe_actionable_http_errors(string operation, AiProviderFailure failure, HttpStatusCode expected)
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        await ItemAsync(client, s, "Work", "Todo");
        fixture.Provider.Tasks = fixture.Provider.Summary = _ => throw new AiProviderException(failure, 7);
        using var response = await client.PostAsJsonAsync(s.AiPath + "/" + operation, new AiTaskProposalRequestDto("Feature"));
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        if (failure == AiProviderFailure.RateLimited) Assert.Equal(TimeSpan.FromSeconds(7), response.Headers.RetryAfter!.Delta);
    }

    [PostgresTheory]
    [InlineData("{")]
    [InlineData("{\"tasks\":[]}")]
    [InlineData("{\"tasks\":[{\"id\":\"task\",\"title\":\"Task\",\"priority\":\"Low\",\"dependsOn\":[\"unknown\"]}]}")]
    public async Task Bad_provider_output_is_never_previewed_or_saved(string output)
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        fixture.Provider.Tasks = _ => Task.FromResult(output);
        Assert.Equal(HttpStatusCode.BadGateway, (await client.PostAsJsonAsync(s.AiPath + "/tasks/propose", new AiTaskProposalRequestDto("Feature"))).StatusCode);
        await fixture.InDb(async db => Assert.False(await db.WorkItems.AnyAsync(x => x.ProjectId == s.Project.Id)));
    }

    [PostgresFact]
    public async Task Invalid_summary_and_unexpected_provider_errors_do_not_leak_messages()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner); await ItemAsync(client, s, "Work", "Todo");
        fixture.Provider.Summary = _ => Task.FromResult("{\"overview\":\"Only one field\"}");
        Assert.Equal(HttpStatusCode.BadGateway, (await client.PostAsJsonAsync(s.AiPath + "/summary", new { })).StatusCode);
        fixture.Provider.Tasks = _ => throw new InvalidOperationException("private-provider-secret-and-project-data");
        using var error = await client.PostAsJsonAsync(s.AiPath + "/tasks/propose", new AiTaskProposalRequestDto("Feature"));
        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.DoesNotContain("private-provider-secret", await error.Content.ReadAsStringAsync());
    }

    [PostgresFact]
    public async Task Timeout_cancels_cooperative_provider_and_bounds_noncooperative_calls()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.Tasks = async ct =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            return TestAiProvider.ValidTasks;
        };
        Assert.Equal(HttpStatusCode.GatewayTimeout, (await client.PostAsJsonAsync(s.AiPath + "/tasks/propose", new AiTaskProposalRequestDto("Feature"))).StatusCode);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.Tasks = _ => pending.Task;
        try
        {
            Assert.Equal(HttpStatusCode.GatewayTimeout, (await client.PostAsJsonAsync(s.AiPath + "/tasks/propose", new AiTaskProposalRequestDto("Feature"))).StatusCode);
        }
        finally { pending.TrySetResult(TestAiProvider.ValidTasks); }
    }

    [PostgresFact]
    public async Task Generation_rate_limit_and_input_validation_apply_before_provider_invocation()
    {
        var s = await SetupAsync(); using var invalid = fixture.Client(s.Owner);
        using var bad = await invalid.PostAsJsonAsync(s.AiPath + "/tasks/propose", new AiTaskProposalRequestDto(" "));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode); Assert.Equal(0, fixture.Provider.Calls);
        using var json = JsonDocument.Parse(await bad.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("featureDescription", out _));
        using var limited = fixture.Client(s.Owner);
        for (var i = 0; i < 10; i++) await PreviewAsync(limited, s);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await limited.PostAsJsonAsync(s.AiPath + "/tasks/propose", new AiTaskProposalRequestDto("Feature"))).StatusCode);
        Assert.Equal(10, fixture.Provider.Calls);
    }

    [PostgresFact]
    public async Task Client_cancellation_reaches_provider_without_creating_tasks()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.Tasks = async ct =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            return TestAiProvider.ValidTasks;
        };
        using var cancellation = new CancellationTokenSource();
        var request = client.PostAsJsonAsync(s.AiPath + "/tasks/propose", new AiTaskProposalRequestDto("Feature"), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.InDb(async db => Assert.False(await db.WorkItems.AnyAsync(x => x.ProjectId == s.Project.Id)));
    }

    [PostgresFact]
    public async Task Openapi_documents_all_three_ai_operations()
    {
        using var client = fixture.Client();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        foreach (var suffix in new[] { "/tasks/propose", "/tasks/accept", "/summary" })
            Assert.True(json.RootElement.GetProperty("paths").GetProperty(
                "/api/v1/workspaces/{workspaceId}/projects/{projectId}/ai" + suffix).TryGetProperty("post", out _));
    }

    private async Task<Scope> SetupAsync(WorkspaceRole role = WorkspaceRole.Developer, bool assigned = true)
    {
        fixture.Provider.Reset(); fixture.Clock.Now = DateTimeOffset.UtcNow;
        var owner = await fixture.UserAsync(); var member = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (member, role));
        using var client = fixture.Client(owner);
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/projects", new CreateProjectRequestDto("AI test", "AIT"));
        response.EnsureSuccessStatusCode();
        var project = (await response.Content.ReadFromJsonAsync<ProjectResponseDto>())!;
        if (assigned) Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspace.Id}/projects/{project.Id}/members", new ProjectMemberRequestDto(member.Id))).StatusCode);
        return new(owner, member, workspace, project);
    }

    private static async Task<AiTaskPreviewDto> PreviewAsync(HttpClient client, Scope s)
    {
        using var response = await client.PostAsJsonAsync(s.AiPath + "/tasks/propose", new AiTaskProposalRequestDto("Implement feature"));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<AiTaskPreviewDto>())!;
    }

    private static async Task<AiAcceptedTasksDto> AcceptAsync(HttpClient client, Scope s, AiTaskPreviewDto preview, IReadOnlyList<AiTaskProposal>? tasks = null)
    {
        using var response = await client.PostAsJsonAsync(s.AiPath + "/tasks/accept", new AiTaskAcceptanceRequestDto(preview.PreviewToken, true, tasks ?? preview.Tasks));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<AiAcceptedTasksDto>())!;
    }

    private static async Task<WorkItemDto> ItemAsync(HttpClient client, Scope s, string title, string status, DateTime? due = null)
    {
        using var response = await client.PostAsJsonAsync(s.ProjectPath + "/work-items", new WorkItemWriteDto(title, "Project context", "Task", status,
            "Medium", null, null, null, due == null ? null : DateOnly.FromDateTime(due.Value), null, [], null));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<WorkItemDto>())!;
    }

    private sealed record Scope(User Owner, User Member, Workspace Workspace, ProjectResponseDto Project)
    {
        public string ProjectPath => $"/api/v1/workspaces/{Workspace.Id}/projects/{Project.Id}";
        public string AiPath => ProjectPath + "/ai";
    }
}
