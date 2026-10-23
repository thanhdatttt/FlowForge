using System.Net;
using System.Net.Http.Json;
using FlowForge.Api.Contracts.Projects;
using FlowForge.Api.Contracts.WorkItems;
using FlowForge.Api.Contracts.Workflows;
using FlowForge.Domain.Identity;
using FlowForge.Domain.Projects;
using FlowForge.Domain.WorkItems;
using FlowForge.Domain.Workflows;
using FlowForge.Domain.Workspaces;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Notifications;
using FlowForge.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseFiveIntegrationTests(PhaseFiveFixture fixture) : IClassFixture<PhaseFiveFixture>
{
    [PostgresTheory]
    [InlineData("WorkItemCreated")]
    [InlineData("WorkItemUpdated")]
    [InlineData("WorkItemStatusChanged")]
    [InlineData("WorkItemAssignmentChanged")]
    [InlineData("CommentAdded")]
    public async Task Api_events_schedule_and_execute_matching_rules(string trigger)
    {
        var s = await SetupAsync();
        using var client = fixture.Client(s.Owner);
        var item = trigger == "WorkItemCreated" ? null : await ItemAsync(client, s);
        var workflow = await WorkflowAsync(client, s, trigger, [Action("ChangePriority", value: "High")]);
        item ??= await ItemAsync(client, s);
        if (trigger == "CommentAdded")
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(s.ProjectPath + $"/work-items/{item.Id}/comments", new CommentWriteDto("Trigger", null))).StatusCode);
        else if (trigger != "WorkItemCreated")
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(s.ProjectPath + $"/work-items/{item.Id}",
                Write(s.Member.Id, "InProgress", item.Version))).StatusCode);
        await DrainAsync();
        var history = await client.GetFromJsonAsync<PageDto<WorkflowExecutionDto>>(s.WorkflowPath + $"/{workflow.Id}/executions");
        var execution = Assert.Single(history!.Items);
        Assert.Equal("Succeeded", execution.Status); Assert.True(execution.ConditionsMatched);
        var detail = await client.GetFromJsonAsync<WorkflowExecutionDto>(s.WorkflowPath + $"/{workflow.Id}/executions/{execution.Id}");
        Assert.Equal("Succeeded", Assert.Single(detail!.Actions).Status);
        await fixture.InDb(async db => Assert.Equal("High", (await db.WorkItems.SingleAsync(x => x.Id == item.Id)).Priority));
    }

    [PostgresFact]
    public async Task Execution_lookup_by_id_preserves_workflow_authorization_and_workspace_isolation()
    {
        var s = await SetupAsync();
        using var owner = fixture.Client(s.Owner);
        var workflow = await WorkflowAsync(owner, s, "WorkItemCreated", [Action("Unassign")]);
        await ItemAsync(owner, s);
        await DrainAsync();
        var history = await owner.GetFromJsonAsync<PageDto<WorkflowExecutionDto>>(s.WorkflowPath + $"/{workflow.Id}/executions");
        var expected = Assert.Single(history!.Items);
        var path = s.WorkflowPath + $"/executions/{expected.Id}";
        var actual = await owner.GetFromJsonAsync<WorkflowExecutionDto>(path);
        Assert.Equal(expected.Id, actual!.Id);
        Assert.Equal(workflow.Id, actual.WorkflowId);
        Assert.Equal(expected.WorkflowVersionId, actual.WorkflowVersionId);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Definition.Trigger, actual.Definition.Trigger);
        Assert.Equal(expected.Actions.Select(x => x.ActionId), actual.Actions.Select(x => x.ActionId));
        using var developer = fixture.Client(s.Member);
        using var anonymous = fixture.Client();
        var stranger = await fixture.UserAsync();
        using var outsider = fixture.Client(stranger);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await developer.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(s.WorkflowPath + $"/executions/{Guid.NewGuid()}")).StatusCode);
        var other = await SetupAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(other.WorkflowPath + $"/executions/{expected.Id}")).StatusCode);
        var manager = await fixture.UserAsync();
        await fixture.InDb(async db =>
        {
            var membership = new WorkspaceMembership { WorkspaceId = s.Project.WorkspaceId, UserId = manager.Id, Role = WorkspaceRole.Manager };
            db.WorkspaceMemberships.Add(membership);
            db.ProjectMemberships.Add(new ProjectMembership { WorkspaceId = s.Project.WorkspaceId, ProjectId = s.Project.Id, WorkspaceMembershipId = membership.Id });
            await db.SaveChangesAsync();
        });
        using var managerClient = fixture.Client(manager);
        Assert.Equal(HttpStatusCode.OK, (await managerClient.GetAsync(path)).StatusCode);
        var workspaceRule = await WorkflowAsync(owner, s, "WorkItemCreated", [Action("Unassign")], workspaceScope: true);
        await ItemAsync(owner, s); await DrainAsync();
        var workspaceHistory = await owner.GetFromJsonAsync<PageDto<WorkflowExecutionDto>>(s.WorkflowPath + $"/{workspaceRule.Id}/executions");
        Assert.Equal(HttpStatusCode.NotFound, (await managerClient.GetAsync(s.WorkflowPath + $"/executions/{Assert.Single(workspaceHistory!.Items).Id}")).StatusCode);
        await fixture.InDb(async db =>
        {
            var membership = await db.ProjectMemberships.SingleAsync(x => x.ProjectId == s.Project.Id && x.WorkspaceMembership.UserId == manager.Id);
            membership.RemovedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await managerClient.GetAsync(path)).StatusCode);
    }

    [PostgresFact]
    public async Task All_ordered_actions_preserve_versions_audit_mentions_notifications_and_email()
    {
        var s = await SetupAsync();
        using var client = fixture.Client(s.Owner);
        await fixture.InDb(async db => { db.WorkLabels.Add(new WorkLabel { ProjectId = s.Project.Id, Name = "bug" }); await db.SaveChangesAsync(); });
        var actions = new[] { Action("Assign", s.Member.Id), Action("ChangeStatus", value: "InProgress"), Action("ChangePriority", value: "Critical"),
            Action("AddLabel", value: "bug"), Action("RemoveLabel", value: "bug"), Action("AddComment", value: $"Please review @{{{s.Member.Id}}}"),
            Action("Notify", s.Member.Id), Action("SendEmail", s.Member.Id), Action("Unassign") };
        var workflow = await WorkflowAsync(client, s, "WorkItemCreated", actions);
        var item = await ItemAsync(client, s);
        await DrainAsync();
        await fixture.InDb(async db =>
        {
            var current = await db.WorkItems.SingleAsync(x => x.Id == item.Id);
            Assert.Equal("Critical", current.Priority); Assert.Null(current.AssigneeMembershipId); Assert.Equal(item.Version + 6, current.Version);
            Assert.Equal("InProgress", (await db.ProjectStatuses.SingleAsync(x => x.Id == current.StatusId)).Name);
            Assert.Empty(await db.WorkItemLabels.Where(x => x.WorkItemId == item.Id).ToListAsync());
            var comment = Assert.Single(await db.WorkComments.Where(x => x.WorkItemId == item.Id).ToListAsync());
            Assert.Equal(s.Member.Id, (await db.CommentMentions.SingleAsync(x => x.CommentId == comment.Id)).MentionedUserId);
            var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == workflow.Id);
            Assert.Equal("Succeeded", execution.Status);
            Assert.Equal(actions.Length, await db.WorkflowActionResults.CountAsync(x => x.ExecutionId == execution.Id && x.Status == "Succeeded"));
            Assert.Equal(actions.Length, await db.ActivityRecords.CountAsync(x => x.WorkflowExecutionId == execution.Id && x.Action.StartsWith("workflow.action.")));
            Assert.Single(await db.Notifications.Where(x => x.WorkItemId == item.Id && x.Type == "Workflow").ToListAsync());
            Assert.Single(await db.EmailDeliveries.Where(x => x.WorkflowExecutionId == execution.Id).ToListAsync());
            Assert.All(await db.DomainEvents.Where(x => x.OriginExecutionId == execution.Id).ToListAsync(), x => Assert.Equal(1, x.Depth));
        });
        await DispatchAsync();
        await fixture.InScope(async services => { while (await services.GetRequiredService<EmailDeliveryProcessor>().SendNextAsync(default)) { } });
        Assert.Contains(fixture.Sender.Messages, x => x.Recipient == s.Member.Email && x.Subject == "FlowForge workflow notification");
        Assert.DoesNotContain(fixture.Sender.Messages.Where(x => x.Recipient == s.Member.Email), x => x.Body.Contains("Please review"));
    }

    [PostgresFact]
    public async Task Conditions_read_current_state_once_and_nonmatches_preserve_items()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var matched = await WorkflowAsync(client, s, "WorkItemCreated", [Action("ChangePriority", value: "Critical"), Action("AddComment", value: "Matched")],
            [new("Priority", "Equals", ["High"]), new("Actor", "Equals", [s.Owner.Id.ToString()])]);
        var unmatched = await WorkflowAsync(client, s, "WorkItemCreated", [Action("AddComment", value: "Must not run")], [new("Type", "Equals", ["Issue"])]);
        var item = await ItemAsync(client, s);
        // State changes after the event, before processing, must be observed by conditions.
        await fixture.InDb(async db => { var current = await db.WorkItems.SingleAsync(x => x.Id == item.Id); current.Priority = "High"; await db.SaveChangesAsync(); });
        await DrainAsync();
        await fixture.InDb(async db =>
        {
            Assert.True((await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == matched.Id)).ConditionsMatched);
            var noMatch = await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == unmatched.Id);
            Assert.Equal("Succeeded", noMatch.Status); Assert.False(noMatch.ConditionsMatched);
            Assert.Equal("Cancelled", (await db.WorkflowActionResults.SingleAsync(x => x.ExecutionId == noMatch.Id)).Status);
            Assert.Equal("Matched", Assert.Single(await db.WorkComments.Where(x => x.WorkItemId == item.Id).ToListAsync()).Body);
        });
    }

    [PostgresFact]
    public async Task Management_permissions_validation_scope_and_archived_rules_follow_existing_policies()
    {
        var s = await SetupAsync(); using var owner = fixture.Client(s.Owner); using var developer = fixture.Client(s.Member);
        using var anonymous = fixture.Client(); var stranger = await fixture.UserAsync(); using var outsider = fixture.Client(stranger);
        var manager = await fixture.UserAsync(); var viewer = await fixture.UserAsync();
        await fixture.InDb(async db =>
        {
            foreach (var (user, role) in new[] { (manager, WorkspaceRole.Manager), (viewer, WorkspaceRole.Viewer) })
            {
                var membership = new WorkspaceMembership { WorkspaceId = s.Project.WorkspaceId, UserId = user.Id, Role = role };
                db.WorkspaceMemberships.Add(membership);
                db.ProjectMemberships.Add(new ProjectMembership { WorkspaceId = s.Project.WorkspaceId, ProjectId = s.Project.Id, WorkspaceMembershipId = membership.Id });
            }
            await db.SaveChangesAsync();
        });
        using var managerClient = fixture.Client(manager); using var viewerClient = fixture.Client(viewer);
        var request = new WorkflowWriteDto("Rule", s.Project.Id, new("WorkItemCreated", [], [Action("Unassign")]));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(s.WorkflowPath, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.PostAsJsonAsync(s.WorkflowPath, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.PostAsJsonAsync(s.WorkflowPath, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await managerClient.PostAsJsonAsync(s.WorkflowPath, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await managerClient.PostAsJsonAsync(s.WorkflowPath, request with { ProjectId = null })).StatusCode);
        var workspaceRule = await WorkflowAsync(owner, s, "WorkItemCreated", [Action("Unassign")], workspaceScope: true);
        foreach (var state in new[] { "enable", "disable" })
        {
            var invalid = await owner.PostAsJsonAsync(s.WorkflowPath + $"/{workspaceRule.Id}/" + state, new WorkflowStateDto(0));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var problem = await invalid.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.True(problem.GetProperty("errors").TryGetProperty("version", out _));
        }
        Assert.Equal(HttpStatusCode.NotFound, (await managerClient.GetAsync(s.WorkflowPath + $"/{workspaceRule.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(s.WorkflowPath + $"/{workspaceRule.Id}")).StatusCode);
        Assert.Empty((await developer.GetFromJsonAsync<PageDto<WorkflowDto>>(s.WorkflowPath))!.Items);
        Assert.Single((await managerClient.GetFromJsonAsync<PageDto<WorkflowDto>>(s.WorkflowPath))!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync(s.WorkflowPath + "?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(s.WorkflowPath + "/validate", request with { Definition = new("Bad", [], []) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(s.WorkflowPath + "/validate", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(s.WorkflowPath, request with { Definition = new("WorkItemCreated", [], [Action("Assign", stranger.Id)]) })).StatusCode);
        await owner.PostAsync(s.ProjectPath + "/archive", null);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(s.WorkflowPath, request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync(s.WorkflowPath + $"/{workspaceRule.Id}/executions?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(s.WorkflowPath + $"/{workspaceRule.Id}/executions/{Guid.NewGuid()}")).StatusCode);
    }

    [PostgresFact]
    public async Task Scheduling_pins_immutable_versions_and_rejects_stale_updates()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var workflow = await WorkflowAsync(client, s, "WorkItemCreated", [Action("AddComment", value: "Version one")]);
        var first = await ItemAsync(client, s); await ScheduleAsync();
        var update = new WorkflowWriteDto("Version two", s.Project.Id, new("WorkItemCreated", [], [Action("AddComment", value: "Version two")]), 1);
        var response = await client.PutAsJsonAsync(s.WorkflowPath + $"/{workflow.Id}", update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<WorkflowDto>())!.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(s.WorkflowPath + $"/{workflow.Id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(s.WorkflowPath + $"/{workflow.Id}/enable", new WorkflowStateDto(1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(s.WorkflowPath + $"/{workflow.Id}", update with { Version = 2, ProjectId = null })).StatusCode);
        var second = await ItemAsync(client, s); await DrainAsync();
        var history = (await client.GetFromJsonAsync<PageDto<WorkflowExecutionDto>>(s.WorkflowPath + $"/{workflow.Id}/executions"))!;
        var oldExecution = history.Items.Single(x => x.WorkItemId == first.Id);
        var newExecution = history.Items.Single(x => x.WorkItemId == second.Id);
        Assert.Equal(1, oldExecution.Version); Assert.Equal(2, newExecution.Version);
        Assert.Equal("Version one", Assert.Single(oldExecution.Definition.Actions!).Value);
        Assert.Equal("Version two", Assert.Single(newExecution.Definition.Actions!).Value);
        var detail = (await client.GetFromJsonAsync<WorkflowExecutionDto>(s.WorkflowPath + $"/{workflow.Id}/executions/{oldExecution.Id}"))!;
        Assert.Equal(1, detail.Version); Assert.Equal("Version one", Assert.Single(detail.Definition.Actions!).Value);
        Assert.Equal(s.Owner.Id, detail.ActorUserId); Assert.False(string.IsNullOrWhiteSpace(detail.CorrelationId));
        Assert.Null(detail.OriginExecutionId); Assert.Equal(0, detail.Depth);
        await fixture.InDb(async db =>
        {
            Assert.Equal("Version one", (await db.WorkComments.SingleAsync(x => x.WorkItemId == first.Id)).Body);
            Assert.Equal("Version two", (await db.WorkComments.SingleAsync(x => x.WorkItemId == second.Id)).Body);
            var old = await db.WorkflowVersions.SingleAsync(x => x.WorkflowId == workflow.Id && x.VersionNumber == 1);
            old.Configuration = "{}";
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });
    }

    [PostgresFact]
    public async Task Pre_enable_and_disabled_events_are_not_replayed()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        await ItemAsync(client, s);
        var workflow = await WorkflowAsync(client, s, "WorkItemCreated", [Action("AddComment", value: "Fresh only")]);
        await DrainAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(s.WorkflowPath + $"/{workflow.Id}/disable", new WorkflowStateDto(1))).StatusCode);
        await ItemAsync(client, s);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(s.WorkflowPath + $"/{workflow.Id}/enable", new WorkflowStateDto(1))).StatusCode);
        await DrainAsync();
        var fresh = await ItemAsync(client, s); await DrainAsync();
        await fixture.InDb(async db =>
        {
            Assert.Single(await db.WorkflowExecutions.Where(x => x.WorkflowId == workflow.Id).ToListAsync());
            Assert.Equal(fresh.Id, Assert.Single(await db.WorkComments.Where(x => db.WorkItems.Any(i => i.Id == x.WorkItemId && i.ProjectId == s.Project.Id)).ToListAsync()).WorkItemId);
        });
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_resumes_after_committed_actions_and_terminal_errors_are_safe(bool permanent)
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var workflow = await WorkflowAsync(client, s, "WorkItemCreated", [Action("Assign", s.Member.Id), Action("AddComment", value: "Exactly once")]);
        var item = await ItemAsync(client, s); fixture.Failure.WorkItemId = item.Id; fixture.Failure.Permanent = permanent;
        await DrainAsync();
        await fixture.InDb(async db =>
        {
            var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == workflow.Id);
            Assert.Equal(permanent ? "Failed" : "Pending", execution.Status);
            Assert.DoesNotContain("secret", execution.SafeError);
            Assert.Equal(1, await db.WorkflowActionResults.CountAsync(x => x.ExecutionId == execution.Id && x.Status == "Succeeded"));
            Assert.Empty(await db.WorkComments.Where(x => x.WorkItemId == item.Id).ToListAsync());
            if (!permanent) { execution.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(); }
        });
        await DrainAsync();
        await fixture.InDb(async db =>
        {
            var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == workflow.Id);
            Assert.Equal(permanent ? "Failed" : "Succeeded", execution.Status);
            Assert.Equal(permanent ? 0 : 1, await db.WorkComments.CountAsync(x => x.WorkItemId == item.Id));
            var results = await db.WorkflowActionResults.Where(x => x.ExecutionId == execution.Id).ToListAsync();
            Assert.Equal(1, results.Single(x => x.ActionId == workflow.Definition.Actions![0].Id).AttemptCount);
            Assert.Equal(permanent ? 1 : 2, results.Single(x => x.ActionId == workflow.Definition.Actions![1].Id).AttemptCount);
        });
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disable_or_creator_revocation_cancels_unfinished_actions(bool revoke)
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var manager = await fixture.UserAsync();
        await fixture.InDb(async db =>
        {
            var membership = new WorkspaceMembership { WorkspaceId = s.Project.WorkspaceId, UserId = manager.Id, Role = WorkspaceRole.Manager };
            db.WorkspaceMemberships.Add(membership); db.ProjectMemberships.Add(new ProjectMembership { WorkspaceId = s.Project.WorkspaceId, ProjectId = s.Project.Id, WorkspaceMembershipId = membership.Id });
            await db.SaveChangesAsync();
        });
        using var managerClient = fixture.Client(manager);
        var workflow = await WorkflowAsync(managerClient, s, "WorkItemCreated", [Action("ChangePriority", value: "High"), Action("AddComment", value: "Must be cancelled")]);
        var item = await ItemAsync(client, s); fixture.Failure.WorkItemId = item.Id;
        await DrainAsync();
        if (revoke) await fixture.InDb(async db =>
        {
            var membership = await db.WorkspaceMemberships.SingleAsync(x => x.UserId == manager.Id && x.WorkspaceId == s.Project.WorkspaceId);
            membership.RemovedAt = DateTimeOffset.UtcNow;
            var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == workflow.Id);
            execution.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        });
        else Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(s.WorkflowPath + $"/{workflow.Id}/disable", new WorkflowStateDto(1))).StatusCode);
        await DrainAsync();
        await fixture.InDb(async db =>
        {
            var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == workflow.Id);
            Assert.Equal("Cancelled", execution.Status);
            Assert.Equal("High", (await db.WorkItems.SingleAsync(x => x.Id == item.Id)).Priority);
            Assert.Empty(await db.WorkComments.Where(x => x.WorkItemId == item.Id).ToListAsync());
            Assert.Equal(1, await db.WorkflowActionResults.CountAsync(x => x.ExecutionId == execution.Id && x.Status == "Succeeded"));
            Assert.Equal(1, await db.WorkflowActionResults.CountAsync(x => x.ExecutionId == execution.Id && x.Status == "Cancelled"));
        });
    }

    [PostgresFact]
    public async Task Concurrent_schedulers_and_runners_deduplicate_and_break_multi_workflow_loops()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var a = await WorkflowAsync(client, s, "WorkItemUpdated", [Action("ChangePriority", value: "High")]);
        var b = await WorkflowAsync(client, s, "WorkItemUpdated", [Action("ChangePriority", value: "Low")]);
        var item = await ItemAsync(client, s);
        await client.PutAsJsonAsync(s.ProjectPath + $"/work-items/{item.Id}", Write(null, "Todo", item.Version));
        await Task.WhenAll(ScheduleAsync(), ScheduleAsync());
        await Task.WhenAll(RunAsync(), RunAsync()); await DrainAsync();
        await fixture.InDb(async db =>
        {
            var executions = await db.WorkflowExecutions.Where(x => x.WorkflowId == a.Id || x.WorkflowId == b.Id).ToListAsync();
            Assert.Equal(4, executions.Count); Assert.All(executions, x => Assert.Equal("Succeeded", x.Status));
            Assert.All(await db.DomainEvents.Where(x => x.ProjectId == s.Project.Id).ToListAsync(), x => Assert.InRange(x.Depth, 0, 2));
            await db.DomainEvents.Where(x => x.ProjectId == s.Project.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.WorkflowProcessedAt, (DateTimeOffset?)null));
        });
        await DrainAsync();
        await fixture.InDb(async db => Assert.Equal(4, await db.WorkflowExecutions.CountAsync(x => x.WorkflowId == a.Id || x.WorkflowId == b.Id)));
    }

    [PostgresFact]
    public async Task Expired_claims_recover_and_exhausted_claims_finish_without_duplicate_effects()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var workflow = await WorkflowAsync(client, s, "WorkItemCreated", [Action("AddComment", value: "Recovered")]);
        var item = await ItemAsync(client, s); await ScheduleAsync();
        await fixture.InDb(async db =>
        {
            var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == workflow.Id);
            execution.Status = "Running"; execution.LockId = Guid.NewGuid(); execution.AttemptCount = 1;
            execution.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-3); await db.SaveChangesAsync();
        });
        await DrainAsync();
        await fixture.InDb(async db => Assert.Single(await db.WorkComments.Where(x => x.WorkItemId == item.Id).ToListAsync()));
        var exhausted = await ItemAsync(client, s); await ScheduleAsync();
        await fixture.InDb(async db =>
        {
            var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkItemId == exhausted.Id);
            execution.Status = "Running"; execution.LockId = Guid.NewGuid(); execution.AttemptCount = 5;
            execution.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-3); await db.SaveChangesAsync();
        });
        await DrainAsync();
        await fixture.InDb(async db =>
        {
            var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkItemId == exhausted.Id);
            Assert.Equal("Failed", execution.Status);
            Assert.Equal(5, execution.AttemptCount);
            Assert.Empty(await db.WorkComments.Where(x => x.WorkItemId == exhausted.Id).ToListAsync());
        });
    }

    [PostgresFact]
    public async Task Date_triggers_include_unassigned_work_and_deduplicate_without_notification_side_effects()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var due = await WorkflowAsync(client, s, "WorkItemDueDateReached", [Action("AddComment", value: "Due")]);
        var overdue = await WorkflowAsync(client, s, "WorkItemDueDateOverdue", [Action("AddComment", value: "Overdue")]);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var dueItem = await ItemAsync(client, s, today); var lateItem = await ItemAsync(client, s, today.AddDays(-1));
        await ItemAsync(client, s, today.AddDays(1));
        var done = await ItemAsync(client, s, today.AddDays(-1), "Done");
        var archived = await ItemAsync(client, s, today.AddDays(-1)); await client.PostAsync(s.ProjectPath + $"/work-items/{archived.Id}/archive", null);
        await Task.WhenAll(fixture.InScope(async services => await services.GetRequiredService<WorkflowDateScanner>().ScanAsync(default)),
            fixture.InScope(async services => await services.GetRequiredService<WorkflowDateScanner>().ScanAsync(default)));
        await DrainAsync();
        await fixture.InScope(async services => Assert.Equal(0, await services.GetRequiredService<WorkflowDateScanner>().ScanAsync(default)));
        await fixture.InDb(async db =>
        {
            Assert.Equal("Due", (await db.WorkComments.SingleAsync(x => x.WorkItemId == dueItem.Id)).Body);
            Assert.Equal("Overdue", (await db.WorkComments.SingleAsync(x => x.WorkItemId == lateItem.Id)).Body);
            Assert.Empty(await db.WorkComments.Where(x => x.WorkItemId == done.Id || x.WorkItemId == archived.Id).ToListAsync());
            Assert.Empty(await db.Notifications.Where(x => x.ProjectId == s.Project.Id).ToListAsync());
            Assert.Equal(2, await db.WorkflowExecutions.CountAsync(x => x.WorkflowId == due.Id || x.WorkflowId == overdue.Id));
        });
    }

    [PostgresFact]
    public async Task Notifications_cannot_consume_workflow_events_and_rollback_leaves_no_scheduled_execution()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var workflow = await WorkflowAsync(client, s, "WorkItemCreated", [Action("AddComment", value: "Independent")], workspaceScope: true);
        var item = await ItemAsync(client, s); await DispatchAsync(); await DrainAsync();
        await fixture.InDb(async db =>
        {
            Assert.Single(await db.WorkflowExecutions.Where(x => x.WorkflowId == workflow.Id).ToListAsync());
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.DomainEvents.Add(new DomainEvent { WorkspaceId = s.Project.WorkspaceId, ProjectId = s.Project.Id, EntityId = item.Id,
                EventType = "WorkItemCreated", Payload = "{}", CorrelationId = "rolled-back" });
            await db.SaveChangesAsync(); await transaction.RollbackAsync();
        });
        await DrainAsync();
        await fixture.InDb(async db => Assert.Single(await db.WorkflowExecutions.Where(x => x.WorkflowId == workflow.Id).ToListAsync()));
    }

    [PostgresFact]
    public async Task Queued_workflow_email_rechecks_recipient_access_and_missing_smtp_is_diagnosable()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var workflow = await WorkflowAsync(client, s, "WorkItemCreated", [Action("SendEmail", s.Member.Id)]);
        var item = await ItemAsync(client, s); await DrainAsync();
        await fixture.InDb(async db => { var membership = await db.ProjectMemberships.SingleAsync(x => x.ProjectId == s.Project.Id); membership.RemovedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); });
        await fixture.InScope(async services => { while (await services.GetRequiredService<EmailDeliveryProcessor>().SendNextAsync(default)) { } });
        await fixture.InDb(async db => Assert.Equal("Cancelled", (await db.EmailDeliveries.SingleAsync(x => db.WorkflowExecutions.Any(e => e.Id == x.WorkflowExecutionId && e.WorkItemId == item.Id))).Status));
        // A self-recipient remains eligible, allowing a separate configuration-unavailable test.
        var emailRule = await WorkflowAsync(client, s, "WorkItemCreated", [Action("SendEmail", s.Owner.Id)]);
        fixture.Sender.IsConfigured = false;
        try
        {
            var noSmtp = await ItemAsync(client, s); await DrainAsync();
            await fixture.InDb(async db =>
            {
                var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == emailRule.Id && x.WorkItemId == noSmtp.Id);
                Assert.Equal("Failed", execution.Status); Assert.Equal("Email delivery is not configured.", execution.SafeError);
            });
        }
        finally { fixture.Sender.IsConfigured = true; }
    }


    [PostgresFact]
    public async Task Transient_retries_stop_at_limit_and_never_repeat_completed_actions()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var workflow = await WorkflowAsync(client, s, "WorkItemCreated", [Action("Assign", s.Member.Id), Action("AddComment", value: "Failing storage")]);
        var item = await ItemAsync(client, s); fixture.Failure.WorkItemId = item.Id; fixture.Failure.FailEveryTime = true;
        try
        {
            for (var i = 0; i < 5; i++)
            {
                await DrainAsync();
                if (i < 4) await fixture.InDb(async db =>
                {
                    var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == workflow.Id);
                    Assert.Equal("Pending", execution.Status);
                    execution.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
                });
            }
            await fixture.InDb(async db =>
            {
                var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == workflow.Id);
                Assert.Equal("Failed", execution.Status); Assert.Equal(5, execution.AttemptCount);
                var results = await db.WorkflowActionResults.Where(x => x.ExecutionId == execution.Id).ToListAsync();
                Assert.Equal(1, results.Single(x => x.ActionId == workflow.Definition.Actions![0].Id).AttemptCount);
                Assert.Equal(5, results.Single(x => x.ActionId == workflow.Definition.Actions![1].Id).AttemptCount);
                Assert.Empty(await db.WorkComments.Where(x => x.WorkItemId == item.Id).ToListAsync());
            });
        }
        finally { fixture.Failure.WorkItemId = null; fixture.Failure.FailEveryTime = false; }
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scheduled_work_cancels_when_item_or_project_is_archived(bool project)
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var workflow = await WorkflowAsync(client, s, "WorkItemCreated", [Action("AddComment", value: "Archived")]);
        var item = await ItemAsync(client, s); await ScheduleAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(project ? s.ProjectPath + "/archive" : s.ProjectPath + $"/work-items/{item.Id}/archive", null)).StatusCode);
        await DrainAsync();
        await fixture.InDb(async db =>
        {
            Assert.Equal("Cancelled", (await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == workflow.Id)).Status);
            Assert.Empty(await db.WorkComments.Where(x => x.WorkItemId == item.Id).ToListAsync());
        });
    }

    [PostgresFact]
    public async Task Depth_limits_and_poison_events_do_not_starve_later_valid_events()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var item = await ItemAsync(client, s);
        var created = await WorkflowAsync(client, s, "WorkItemCreated", [Action("AddComment", value: "Valid event")]);
        await WorkflowAsync(client, s, "CommentAdded", [Action("ChangePriority", value: "High")]);
        var poisonId = Guid.NewGuid();
        await fixture.InDb(async db =>
        {
            db.DomainEvents.AddRange(new DomainEvent { WorkspaceId = s.Project.WorkspaceId, ProjectId = s.Project.Id, EntityId = item.Id,
                EventType = "WorkItemCreated", Depth = 8, CorrelationId = "too-deep", Payload = "{}" },
                new DomainEvent { Id = poisonId, WorkspaceId = s.Project.WorkspaceId, ProjectId = s.Project.Id, EntityId = Guid.NewGuid(),
                    EventType = "CommentAdded", CorrelationId = "poison", Payload = "{}" });
            await db.SaveChangesAsync();
        });
        var validItem = await ItemAsync(client, s); await DrainAsync();
        await fixture.InDb(async db =>
        {
            Assert.NotNull((await db.DomainEvents.SingleAsync(x => x.Id == poisonId)).WorkflowProcessedAt);
            Assert.Single(await db.WorkflowExecutions.Where(x => x.WorkflowId == created.Id).ToListAsync());
            Assert.Equal(validItem.Id, (await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == created.Id)).WorkItemId);
        });
    }

    [PostgresFact]
    public async Task Enabling_revalidates_targets_and_cross_workspace_scope_cannot_be_referenced()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var otherOwner = await fixture.UserAsync(); var otherWorkspace = await fixture.WorkspaceAsync(otherOwner);
        using var other = fixture.Client(otherOwner);
        var otherResponse = await other.PostAsJsonAsync($"/api/v1/workspaces/{otherWorkspace.Id}/projects", new { name = "Other", projectKey = "OTHER" });
        var otherProject = (await otherResponse.Content.ReadFromJsonAsync<ProjectResponseDto>())!;
        var request = new WorkflowWriteDto("Scope", otherProject.Id, new("WorkItemCreated", [], [Action("Unassign")]));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(s.WorkflowPath, request)).StatusCode);
        request = request with { ProjectId = s.Project.Id, Definition = new("WorkItemCreated", [], [Action("Assign", s.Member.Id)]) };
        var response = await client.PostAsJsonAsync(s.WorkflowPath, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var draft = (await response.Content.ReadFromJsonAsync<WorkflowDto>())!;
        await client.DeleteAsync(s.ProjectPath + $"/members/{s.Member.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(s.WorkflowPath + $"/{draft.Id}/enable", new WorkflowStateDto(1))).StatusCode);
        request = request with { Definition = new("WorkItemCreated", [], [Action("ChangeStatus", value: "Missing")]) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(s.WorkflowPath, request)).StatusCode);
        request = request with { Definition = new("WorkItemCreated", [], [Action("AddLabel", value: "Missing")]) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(s.WorkflowPath, request)).StatusCode);
    }

    [PostgresFact]
    public async Task Hosted_worker_schedules_executes_and_scans_dates_through_registered_services()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var workflow = await WorkflowAsync(client, s, "WorkItemCreated", [Action("AddComment", value: "Hosted")]);
        var dateWorkflow = await WorkflowAsync(client, s, "WorkItemDueDateOverdue", [Action("ChangePriority", value: "High")]);
        var item = await ItemAsync(client, s, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        using var worker = new WorkflowWorker(fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new WorkflowJobOptions { PollSeconds = 1 }), TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkflowWorker>.Instance);
        await worker.StartAsync(default);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                var complete = false;
                await fixture.InDb(async db => complete = await db.WorkflowExecutions.CountAsync(x =>
                    (x.WorkflowId == workflow.Id || x.WorkflowId == dateWorkflow.Id) && x.Status == "Succeeded") == 2);
                if (complete) break;
                await Task.Delay(50, timeout.Token);
            }
            await fixture.InDb(async db => Assert.Equal("High", (await db.WorkItems.SingleAsync(x => x.Id == item.Id)).Priority));
        }
        finally { await worker.StopAsync(default); }
    }


    [PostgresFact]
    public async Task Assignment_and_unassignment_events_remain_independent_when_http_trace_ids_are_reused()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var item = await ItemAsync(client, s);
        var assigned = await client.PutAsJsonAsync(s.ProjectPath + $"/work-items/{item.Id}", Write(s.Member.Id, version: item.Version));
        item = (await assigned.Content.ReadFromJsonAsync<WorkItemDto>())!;
        var workflow = await WorkflowAsync(client, s, "WorkItemAssignmentChanged", [Action("AddComment", value: "Assignment changed")]);
        client.DefaultRequestHeaders.Add("traceparent", "00-11111111111111111111111111111111-2222222222222222-01");
        var unassigned = await client.PutAsJsonAsync(s.ProjectPath + $"/work-items/{item.Id}", Write(version: item.Version));
        Assert.Equal(HttpStatusCode.OK, unassigned.StatusCode);
        item = (await unassigned.Content.ReadFromJsonAsync<WorkItemDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(s.ProjectPath + $"/work-items/{item.Id}", Write(s.Member.Id, version: item.Version))).StatusCode);
        await DrainAsync();
        await fixture.InDb(async db =>
        {
            Assert.Equal(2, await db.WorkflowExecutions.CountAsync(x => x.WorkflowId == workflow.Id && x.Status == "Succeeded"));
            Assert.Equal(2, await db.WorkComments.CountAsync(x => x.WorkItemId == item.Id));
        });
    }


    [PostgresFact]
    public async Task Comment_action_mentions_are_validated_before_creation_and_enablement()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var stranger = await fixture.UserAsync();
        var definition = new WorkflowDefinition("WorkItemCreated", [], [Action("AddComment", value: $"Hello @{{{stranger.Id}}}")]);
        var request = new WorkflowWriteDto("Mention validation", s.Project.Id, definition);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(s.WorkflowPath + "/validate", request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(s.WorkflowPath, request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(s.WorkflowPath, request with { ProjectId = null })).StatusCode);
        request = request with { Definition = new("WorkItemCreated", [], [Action("AddComment", value: $"Hello @{{{s.Member.Id}}}")]) };
        var response = await client.PostAsJsonAsync(s.WorkflowPath, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var workflow = (await response.Content.ReadFromJsonAsync<WorkflowDto>())!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(s.ProjectPath + $"/members/{s.Member.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(s.WorkflowPath + $"/{workflow.Id}/enable", new WorkflowStateDto(1))).StatusCode);
    }


    [PostgresFact]
    public async Task Exhausted_claim_with_committed_last_action_finishes_without_repeating_it()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var workflow = await WorkflowAsync(client, s, "WorkItemCreated", [Action("AddComment", value: "Committed before crash")]);
        var item = await ItemAsync(client, s); await ScheduleAsync();
        await fixture.InDb(async db =>
        {
            var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == workflow.Id);
            execution.Status = "Running"; execution.LockId = Guid.NewGuid(); execution.AttemptCount = 5;
            execution.ConditionsMatched = true; execution.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-3);
            var result = await db.WorkflowActionResults.SingleAsync(x => x.ExecutionId == execution.Id);
            result.Status = "Succeeded"; result.AttemptCount = 1; result.FinishedAt = DateTimeOffset.UtcNow;
            db.WorkComments.Add(new WorkComment { WorkItemId = item.Id, AuthorUserId = s.Owner.Id, Body = "Committed before crash" });
            await db.SaveChangesAsync();
        });
        await DrainAsync();
        await fixture.InDb(async db =>
        {
            var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == workflow.Id);
            Assert.Equal("Succeeded", execution.Status); Assert.Equal(5, execution.AttemptCount);
            Assert.Single(await db.WorkComments.Where(x => x.WorkItemId == item.Id).ToListAsync());
            Assert.Equal(1, (await db.WorkflowActionResults.SingleAsync(x => x.ExecutionId == execution.Id)).AttemptCount);
        });
    }

    [PostgresFact]
    public async Task Cancelled_worker_attempt_recovers_after_lease_without_repeating_committed_actions()
    {
        var s = await SetupAsync(); using var client = fixture.Client(s.Owner);
        var workflow = await WorkflowAsync(client, s, "WorkItemCreated", [Action("Assign", s.Member.Id), Action("AddComment", value: "After timeout")]);
        var item = await ItemAsync(client, s); await ScheduleAsync();
        fixture.Failure.WorkItemId = item.Id; fixture.Failure.BlockUntilCancelled = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await fixture.InScope(async services => await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => services.GetRequiredService<WorkflowRunner>().RunNextAsync(timeout.Token)));
        }
        finally { fixture.Failure.WorkItemId = null; fixture.Failure.BlockUntilCancelled = false; }
        await fixture.InDb(async db =>
        {
            var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == workflow.Id);
            Assert.Equal("Running", execution.Status); Assert.Equal(1, execution.AttemptCount); Assert.NotNull(execution.LockId);
            Assert.Equal(1, await db.WorkflowActionResults.CountAsync(x => x.ExecutionId == execution.Id && x.Status == "Succeeded"));
            Assert.Empty(await db.WorkComments.Where(x => x.WorkItemId == item.Id).ToListAsync());
            execution.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-3); await db.SaveChangesAsync();
        });
        await DrainAsync();
        await fixture.InDb(async db =>
        {
            var execution = await db.WorkflowExecutions.SingleAsync(x => x.WorkflowId == workflow.Id);
            Assert.Equal("Succeeded", execution.Status); Assert.Equal(2, execution.AttemptCount);
            Assert.Single(await db.WorkComments.Where(x => x.WorkItemId == item.Id).ToListAsync());
            Assert.Equal(1, (await db.WorkflowActionResults.SingleAsync(x => x.ExecutionId == execution.Id && x.ActionId == workflow.Definition.Actions![0].Id)).AttemptCount);
        });
    }

    private static WorkflowAction Action(string type, Guid? user = null, string? value = null) => new(Guid.NewGuid(), type, user, value);
    private static WorkItemWriteDto Write(Guid? assignee = null, string status = "Todo", long? version = null, DateOnly? due = null) =>
        new("Workflow item", null, "Task", status, "Medium", assignee, null, null, due, null, [], version);
    private async Task<WorkItemDto> ItemAsync(HttpClient client, Scenario s, DateOnly? due = null, string status = "Todo")
    {
        var response = await client.PostAsJsonAsync(s.ProjectPath + "/work-items", Write(status: status, due: due));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<WorkItemDto>())!;
    }
    private async Task<WorkflowDto> WorkflowAsync(HttpClient client, Scenario s, string trigger, IReadOnlyList<WorkflowAction> actions,
        IReadOnlyList<WorkflowCondition>? conditions = null, bool workspaceScope = false)
    {
        var response = await client.PostAsJsonAsync(s.WorkflowPath, new WorkflowWriteDto("Rule", workspaceScope ? null : s.Project.Id, new(trigger, conditions ?? [], actions)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var workflow = (await response.Content.ReadFromJsonAsync<WorkflowDto>())!;
        Assert.False(workflow.IsEnabled);
        var enabled = await client.PostAsJsonAsync(s.WorkflowPath + $"/{workflow.Id}/enable", new WorkflowStateDto(1));
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode); return (await enabled.Content.ReadFromJsonAsync<WorkflowDto>())!;
    }
    private Task ScheduleAsync() => fixture.InScope(async services =>
    { for (var i = 0; i < 1000; i++) if (!await services.GetRequiredService<WorkflowScheduler>().ScheduleNextAsync(default)) return; Assert.Fail("Scheduler did not drain."); });
    private Task RunAsync() => fixture.InScope(async services =>
    { for (var i = 0; i < 1000; i++) if (!await services.GetRequiredService<WorkflowRunner>().RunNextAsync(default)) return; Assert.Fail("Runner did not drain."); });
    private async Task DrainAsync()
    {
        for (var i = 0; i < 12; i++)
        {
            await ScheduleAsync(); await RunAsync();
            var pending = false;
            await fixture.InDb(async db => pending = await db.DomainEvents.AnyAsync(x => x.WorkflowProcessedAt == null));
            if (!pending) return;
        }
        Assert.Fail("Workflow chain did not terminate.");
    }
    private Task DispatchAsync() => fixture.InScope(async services =>
    { for (var i = 0; i < 1000; i++) if (!await services.GetRequiredService<NotificationDispatcher>().DispatchNextAsync(default)) return; Assert.Fail("Outbox did not drain."); });
    private async Task<Scenario> SetupAsync()
    {
        fixture.Failure.WorkItemId = null; fixture.Failure.Permanent = false; fixture.Failure.FailEveryTime = false; fixture.Failure.BlockUntilCancelled = false;
        var owner = await fixture.UserAsync(); var member = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (member, WorkspaceRole.Developer));
        using var client = fixture.Client(owner);
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/projects", new { name = "Workflows", projectKey = "WF" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = (await response.Content.ReadFromJsonAsync<ProjectResponseDto>())!;
        await fixture.InDb(async db =>
        {
            var membership = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == workspace.Id && x.UserId == member.Id);
            db.ProjectMemberships.Add(new ProjectMembership { WorkspaceId = workspace.Id, ProjectId = project.Id, WorkspaceMembershipId = membership.Id });
            await db.SaveChangesAsync();
        });
        return new(owner, member, project);
    }
    private sealed record Scenario(User Owner, User Member, ProjectResponseDto Project)
    {
        public string ProjectPath => $"/api/v1/workspaces/{Project.WorkspaceId}/projects/{Project.Id}";
        public string WorkflowPath => $"/api/v1/workspaces/{Project.WorkspaceId}/workflows";
    }
}
