using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowForge.Api.Contracts.Dashboard;
using FlowForge.Api.Contracts.Projects;
using FlowForge.Api.Contracts.WorkItems;
using FlowForge.Domain.Identity;
using FlowForge.Domain.WorkItems;
using FlowForge.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseSixIntegrationTests(PhaseSixFixture fixture) : IClassFixture<PhaseSixFixture>
{
    private static readonly string[] Suffixes = ["", "/workload", "/overdue"];

    [PostgresFact]
    public async Task Empty_project_has_zero_metrics_zero_buckets_and_empty_lists()
    {
        var s = await SetupAsync();
        using var client = fixture.Client(s.Owner);
        var dashboard = await DashboardAsync(client, s);
        Assert.Equal(s.Project.Id, dashboard.ProjectId);
        Assert.Equal(PhaseSixFixture.Now, dashboard.AsOf);
        Assert.Equal(PhaseSixFixture.Today, dashboard.TodayUtc);
        Assert.Equal(0, dashboard.TotalItems);
        Assert.Equal(0, dashboard.CompletedItems);
        Assert.Equal(0m, dashboard.CompletionPercentage);
        Assert.Equal(0, dashboard.OverdueItems);
        Assert.Equal(new[] { "Todo", "InProgress", "InReview", "Done", "Blocked" }, dashboard.CountsByStatus.Select(x => x.Name));
        Assert.Equal(new[] { "Low", "Medium", "High", "Critical" }, dashboard.CountsByPriority.Select(x => x.Priority));
        Assert.All(dashboard.CountsByStatus, x => Assert.Equal(0, x.Count));
        Assert.All(dashboard.CountsByPriority, x => Assert.Equal(0, x.Count));
        var workload = await WorkloadAsync(client, s);
        var overdue = await OverdueAsync(client, s);
        Assert.Empty(workload.Items); Assert.Equal(0, workload.Total);
        Assert.Empty(overdue.Items); Assert.Equal(0, overdue.Total);
        Assert.Equal(1, workload.Page); Assert.Equal(20, workload.PageSize);
        Assert.Equal(1, overdue.Page); Assert.Equal(20, overdue.PageSize);
        Assert.Contains(dashboard.RecentActivity, x => x.Action == "project.created");
    }

    [PostgresFact]
    public async Task Metrics_count_tasks_issues_and_subtasks_once_and_exclude_archived_items()
    {
        var s = await SetupAsync();
        using var client = fixture.Client(s.Owner);
        var parent = await ItemAsync(client, s, "Parent", "Todo", "Low", s.Owner.Id, 60, PhaseSixFixture.Today.AddDays(-1));
        await ItemAsync(client, s, "Issue", "InProgress", "High", s.Member.Id, null, PhaseSixFixture.Today, type: "Issue");
        await ItemAsync(client, s, "Finished", "Done", "Critical", s.Owner.Id, 100, PhaseSixFixture.Today.AddDays(-3));
        await ItemAsync(client, s, "Subtask", "InReview", "Medium", null, 30, PhaseSixFixture.Today.AddDays(-2), parent.Id);
        await ItemAsync(client, s, "Blocked", "Blocked", "High");
        await ItemAsync(client, s, "Zero estimate", "Todo", "Low", s.Owner.Id, 0);
        foreach (var status in new[] { "Todo", "Done" })
        {
            var archived = await ItemAsync(client, s, "Archived " + status, status, "Critical", s.Owner.Id, 999, PhaseSixFixture.Today.AddDays(-10));
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(s.ProjectPath + $"/work-items/{archived.Id}/archive", null)).StatusCode);
        }
        var dashboard = await DashboardAsync(client, s);
        Assert.Equal(6, dashboard.TotalItems);
        Assert.Equal(1, dashboard.CompletedItems);
        Assert.Equal(16.67m, dashboard.CompletionPercentage);
        Assert.Equal(2, dashboard.OverdueItems);
        Assert.Equal(new[] { 2, 1, 1, 1, 1 }, dashboard.CountsByStatus.Select(x => x.Count));
        Assert.Equal(new[] { 2, 1, 2, 1 }, dashboard.CountsByPriority.Select(x => x.Count));
        var groups = await WorkloadAsync(client, s);
        Assert.Equal(3, groups.Total);
        Assert.Equal(5, groups.Items.Sum(x => x.OpenItems));
        var owner = Assert.Single(groups.Items, x => x.AssigneeUserId == s.Owner.Id);
        Assert.Equal(2, owner.OpenItems); Assert.Equal(60, owner.EstimateMinutes);
        var member = Assert.Single(groups.Items, x => x.AssigneeUserId == s.Member.Id);
        Assert.Equal(1, member.OpenItems); Assert.Equal(0, member.EstimateMinutes);
        var unassigned = Assert.Single(groups.Items, x => x.AssigneeUserId == null);
        Assert.Equal("Unassigned", unassigned.DisplayName);
        Assert.Equal(2, unassigned.OpenItems); Assert.Equal(30, unassigned.EstimateMinutes);
        var overdue = await OverdueAsync(client, s);
        Assert.Equal(2, overdue.Total);
        Assert.Equal(new[] { "Subtask", "Parent" }, overdue.Items.Select(x => x.Title));
    }

    [PostgresFact]
    public async Task Completion_uses_status_category_including_inactive_statuses_with_items()
    {
        var s = await SetupAsync();
        using var client = fixture.Client(s.Owner);
        var item = await ItemAsync(client, s, "Released", "Todo", "High", due: PhaseSixFixture.Today.AddDays(-1));
        await fixture.InDb(async db =>
        {
            var status = new ProjectStatus { ProjectId = s.Project.Id, Name = "Released", Category = "Done", SortOrder = 9, IsActive = false };
            db.ProjectStatuses.Add(status);
            (await db.WorkItems.SingleAsync(x => x.Id == item.Id)).StatusId = status.Id;
            db.ProjectStatuses.Add(new ProjectStatus { ProjectId = s.Project.Id, Name = "Unused", Category = "Open", SortOrder = 10, IsActive = false });
            await db.SaveChangesAsync();
        });
        var dashboard = await DashboardAsync(client, s);
        Assert.Equal(1, dashboard.TotalItems); Assert.Equal(1, dashboard.CompletedItems);
        Assert.Equal(100m, dashboard.CompletionPercentage); Assert.Equal(0, dashboard.OverdueItems);
        Assert.Equal(1, Assert.Single(dashboard.CountsByStatus, x => x.Name == "Released").Count);
        Assert.DoesNotContain(dashboard.CountsByStatus, x => x.Name == "Unused");
        Assert.Empty((await WorkloadAsync(client, s)).Items);
        Assert.Empty((await OverdueAsync(client, s)).Items);
    }

    [PostgresFact]
    public async Task Overdue_uses_utc_date_and_includes_unassigned_but_not_today_future_done_or_archived()
    {
        var s = await SetupAsync();
        using var client = fixture.Client(s.Owner);
        var yesterday = await ItemAsync(client, s, "Yesterday", "Todo", "High", due: PhaseSixFixture.Today.AddDays(-1));
        await ItemAsync(client, s, "Today", due: PhaseSixFixture.Today);
        await ItemAsync(client, s, "Tomorrow", due: PhaseSixFixture.Today.AddDays(1));
        await ItemAsync(client, s, "No due date");
        await ItemAsync(client, s, "Already done", "Done", due: PhaseSixFixture.Today.AddDays(-2));
        var archived = await ItemAsync(client, s, "Archived", due: PhaseSixFixture.Today.AddDays(-2));
        await client.PostAsync(s.ProjectPath + $"/work-items/{archived.Id}/archive", null);
        var dashboard = await DashboardAsync(client, s);
        Assert.NotEqual(DateOnly.FromDateTime(PhaseSixFixture.Now.ToOffset(TimeSpan.FromHours(7)).DateTime), dashboard.TodayUtc);
        Assert.Equal(1, dashboard.OverdueItems);
        var row = Assert.Single((await OverdueAsync(client, s)).Items);
        Assert.Equal(yesterday.Id, row.Id); Assert.Equal(yesterday.Key, row.Key);
        Assert.Equal(PhaseSixFixture.Today.AddDays(-1), row.DueDate);
        Assert.Null(row.AssigneeUserId);
    }

    [PostgresFact]
    public async Task Workload_sums_estimates_as_int64_and_does_not_duplicate_items_with_labels_or_dependencies()
    {
        var s = await SetupAsync();
        using var client = fixture.Client(s.Owner);
        var first = await ItemAsync(client, s, "First", assignee: s.Member.Id, estimate: int.MaxValue);
        var second = await ItemAsync(client, s, "Second", assignee: s.Member.Id, estimate: int.MaxValue);
        await fixture.InDb(async db =>
        {
            var a = new WorkLabel { ProjectId = s.Project.Id, Name = "A" };
            var b = new WorkLabel { ProjectId = s.Project.Id, Name = "B" };
            db.WorkLabels.AddRange(a, b);
            db.WorkItemLabels.AddRange(new WorkItemLabel { ProjectId = s.Project.Id, WorkItemId = first.Id, LabelId = a.Id },
                new WorkItemLabel { ProjectId = s.Project.Id, WorkItemId = first.Id, LabelId = b.Id });
            db.WorkItemDependencies.Add(new WorkItemDependency { ProjectId = s.Project.Id, WorkItemId = first.Id, DependsOnItemId = second.Id });
            await db.SaveChangesAsync();
        });
        var workload = Assert.Single((await WorkloadAsync(client, s)).Items);
        Assert.Equal(2, workload.OpenItems);
        Assert.Equal(2L * int.MaxValue, workload.EstimateMinutes);
        Assert.Equal(2, (await DashboardAsync(client, s)).TotalItems);
    }

    [PostgresFact]
    public async Task Committed_updates_archive_restore_and_rollback_are_reflected_without_stored_metrics()
    {
        var s = await SetupAsync();
        using var client = fixture.Client(s.Owner);
        var item = await ItemAsync(client, s, "Original", assignee: s.Owner.Id, estimate: 40, due: PhaseSixFixture.Today.AddDays(-1));
        Assert.Equal(1, (await DashboardAsync(client, s)).OverdueItems);
        await fixture.InDb(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            (await db.WorkItems.SingleAsync(x => x.Id == item.Id)).ArchivedAt = PhaseSixFixture.Now;
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        });
        Assert.Equal(1, (await DashboardAsync(client, s)).TotalItems);
        var changed = await client.PutAsJsonAsync(s.ProjectPath + $"/work-items/{item.Id}",
            new WorkItemWriteDto("Finished", null, "Task", "Done", "Critical", s.Member.Id, null, null,
                item.DueDate, 80, [], item.Version));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var dashboard = await DashboardAsync(client, s);
        Assert.Equal(100m, dashboard.CompletionPercentage); Assert.Equal(0, dashboard.OverdueItems);
        Assert.Empty((await WorkloadAsync(client, s)).Items);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(s.ProjectPath + $"/work-items/{item.Id}/archive", null)).StatusCode);
        Assert.Equal(0, (await DashboardAsync(client, s)).TotalItems);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(s.ProjectPath + $"/work-items/{item.Id}/restore", null)).StatusCode);
        Assert.Equal(100m, (await DashboardAsync(client, s)).CompletionPercentage);
    }

    [PostgresTheory]
    [InlineData(WorkspaceRole.Admin, false, HttpStatusCode.OK)]
    [InlineData(WorkspaceRole.Manager, false, HttpStatusCode.NotFound)]
    [InlineData(WorkspaceRole.Manager, true, HttpStatusCode.OK)]
    [InlineData(WorkspaceRole.Developer, false, HttpStatusCode.NotFound)]
    [InlineData(WorkspaceRole.Developer, true, HttpStatusCode.OK)]
    [InlineData(WorkspaceRole.Viewer, false, HttpStatusCode.NotFound)]
    [InlineData(WorkspaceRole.Viewer, true, HttpStatusCode.OK)]
    public async Task Dashboard_routes_use_existing_project_read_permissions(WorkspaceRole role, bool assigned, HttpStatusCode expected)
    {
        var s = await SetupAsync(role, assigned);
        using var client = fixture.Client(s.Member);
        foreach (var suffix in Suffixes)
            Assert.Equal(expected, (await client.GetAsync(s.DashboardPath + suffix)).StatusCode);
    }

    [PostgresFact]
    public async Task Anonymous_outsiders_removed_and_inactive_members_cannot_read_any_dashboard_route()
    {
        var s = await SetupAsync();
        using var anonymous = fixture.Client();
        using var outsider = fixture.Client(await fixture.UserAsync());
        using var member = fixture.Client(s.Member);
        foreach (var suffix in Suffixes)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(s.DashboardPath + suffix)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(s.DashboardPath + suffix)).StatusCode);
        }
        await fixture.InDb(async db =>
        {
            (await db.ProjectMemberships.SingleAsync(x => x.ProjectId == s.Project.Id)).RemovedAt = PhaseSixFixture.Now;
            await db.SaveChangesAsync();
        });
        foreach (var suffix in Suffixes)
            Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(s.DashboardPath + suffix)).StatusCode);
        await fixture.InDb(async db =>
        {
            (await db.ProjectMemberships.SingleAsync(x => x.ProjectId == s.Project.Id)).RemovedAt = null;
            (await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == s.Workspace.Id && x.UserId == s.Member.Id)).RemovedAt = PhaseSixFixture.Now;
            await db.SaveChangesAsync();
        });
        foreach (var suffix in Suffixes)
            Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(s.DashboardPath + suffix)).StatusCode);
        await fixture.InDb(async db =>
        {
            (await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == s.Workspace.Id && x.UserId == s.Member.Id)).RemovedAt = null;
            (await db.Users.SingleAsync(x => x.Id == s.Member.Id)).IsActive = false;
            await db.SaveChangesAsync();
        });
        foreach (var suffix in Suffixes)
            Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(s.DashboardPath + suffix)).StatusCode);
    }

    [PostgresFact]
    public async Task Metrics_lists_and_activity_are_isolated_from_other_projects_and_workspaces()
    {
        var s = await SetupAsync();
        var other = await SetupAsync();
        using var client = fixture.Client(s.Owner);
        using var otherClient = fixture.Client(other.Owner);
        await ItemAsync(client, s, "Visible", due: PhaseSixFixture.Today.AddDays(-1));
        await ItemAsync(otherClient, other, "Private", "Done", "Critical");
        var sibling = await ProjectAsync(client, s.Workspace.Id, "SIBLING");
        var siblingScope = s with { Project = sibling };
        await ItemAsync(client, siblingScope, "Other project", "Todo", "Critical", s.Owner.Id, 500, PhaseSixFixture.Today.AddDays(-1));
        var dashboard = await DashboardAsync(client, s);
        Assert.Equal(1, dashboard.TotalItems); Assert.Equal(0, dashboard.CompletedItems);
        Assert.Equal(1, dashboard.OverdueItems);
        Assert.All(dashboard.RecentActivity, x => Assert.Equal(s.Project.Id, x.ProjectId));
        Assert.Single((await WorkloadAsync(client, s)).Items);
        Assert.Equal("Visible", Assert.Single((await OverdueAsync(client, s)).Items).Title);
        foreach (var suffix in Suffixes)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(other.DashboardPath + suffix)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
                $"/api/v1/workspaces/{s.Workspace.Id}/projects/{other.Project.Id}/dashboard" + suffix)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
                $"/api/v1/workspaces/{s.Workspace.Id}/projects/{Guid.NewGuid()}/dashboard" + suffix)).StatusCode);
        }
    }

    [PostgresFact]
    public async Task Archived_project_remains_readable_and_reporting_does_not_write_activity_or_events()
    {
        var s = await SetupAsync(WorkspaceRole.Viewer);
        using var owner = fixture.Client(s.Owner);
        using var viewer = fixture.Client(s.Member);
        await ItemAsync(owner, s, "Archived project work", assignee: s.Owner.Id, estimate: 20, due: PhaseSixFixture.Today.AddDays(-1));
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync(s.ProjectPath + "/archive", null)).StatusCode);
        long activityCount = 0, eventCount = 0;
        await fixture.InDb(async db =>
        {
            activityCount = await db.ActivityRecords.LongCountAsync(); eventCount = await db.DomainEvents.LongCountAsync();
        });
        Assert.Equal(1, (await DashboardAsync(viewer, s)).TotalItems);
        Assert.Single((await WorkloadAsync(viewer, s)).Items);
        Assert.Single((await OverdueAsync(viewer, s)).Items);
        await fixture.InDb(async db =>
        {
            Assert.Equal(activityCount, await db.ActivityRecords.LongCountAsync());
            Assert.Equal(eventCount, await db.DomainEvents.LongCountAsync());
        });
    }

    [PostgresFact]
    public async Task Workload_keeps_retained_assignments_instead_of_treating_removed_members_as_unassigned()
    {
        var s = await SetupAsync();
        using var owner = fixture.Client(s.Owner);
        await ItemAsync(owner, s, "Needs reassignment", assignee: s.Member.Id, estimate: 45, due: PhaseSixFixture.Today.AddDays(-1));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync(s.ProjectPath + $"/members/{s.Member.Id}")).StatusCode);
        var row = Assert.Single((await WorkloadAsync(owner, s)).Items);
        Assert.Equal(s.Member.Id, row.AssigneeUserId); Assert.Equal(1, row.OpenItems); Assert.Equal(45, row.EstimateMinutes);
        Assert.Equal(s.Member.Id, Assert.Single((await OverdueAsync(owner, s)).Items).AssigneeUserId);
    }

    [PostgresFact]
    public async Task Recent_activity_is_bounded_ordered_and_matches_authorized_activity_feed()
    {
        var s = await SetupAsync();
        using var client = fixture.Client(s.Owner);
        var item = await ItemAsync(client, s, "With activity");
        await fixture.InDb(async db =>
        {
            for (var i = 0; i < 12; i++)
                db.ActivityRecords.Add(new ActivityRecord
                {
                    WorkspaceId = s.Workspace.Id, ProjectId = s.Project.Id, WorkItemId = item.Id,
                    ActorUserId = s.Owner.Id, Action = "test.update", EntityType = "WorkItem", EntityId = item.Id,
                    OccurredAt = PhaseSixFixture.Now.AddMinutes(i / 2),
                    BeforeValues = "{\"Priority\":\"Low\"}", AfterValues = "{\"Priority\":\"High\"}", CorrelationId = "dashboard-test"
                });
            await db.SaveChangesAsync();
        });
        var dashboard = await DashboardAsync(client, s);
        var feed = (await client.GetFromJsonAsync<PageDto<ActivityDto>>(
            $"/api/v1/workspaces/{s.Workspace.Id}/activity?projectId={s.Project.Id}&pageSize=10"))!;
        Assert.Equal(10, dashboard.RecentActivity.Count);
        Assert.Equal(feed.Items.Select(x => x.Id), dashboard.RecentActivity.Select(x => x.Id));
        Assert.All(dashboard.RecentActivity, x =>
        {
            Assert.Equal("Low", x.BeforeValues!.Value.GetProperty("Priority").GetString());
            Assert.Equal("High", x.AfterValues!.Value.GetProperty("Priority").GetString());
            Assert.Equal("dashboard-test", x.CorrelationId);
        });
    }

    [PostgresFact]
    public async Task Overdue_and_workload_paging_have_stable_order_totals_and_empty_out_of_range_pages()
    {
        var s = await SetupAsync();
        using var client = fixture.Client(s.Owner);
        var first = await ItemAsync(client, s, "First", assignee: s.Owner.Id, due: PhaseSixFixture.Today.AddDays(-1));
        var second = await ItemAsync(client, s, "Second", assignee: s.Member.Id, due: PhaseSixFixture.Today.AddDays(-1));
        var earlier = await ItemAsync(client, s, "Earlier", due: PhaseSixFixture.Today.AddDays(-2));
        var all = await OverdueAsync(client, s);
        Assert.Equal(new[] { earlier.Id, first.Id, second.Id }, all.Items.Select(x => x.Id));
        for (var page = 1; page <= 3; page++)
        {
            var single = (await client.GetFromJsonAsync<PageDto<OverdueWorkItemDto>>(s.DashboardPath + $"/overdue?page={page}&pageSize=1"))!;
            Assert.Equal(3, single.Total); Assert.Equal(page, single.Page); Assert.Equal(1, single.PageSize);
            Assert.Equal(all.Items[page - 1].Id, Assert.Single(single.Items).Id);
        }
        var groups = await WorkloadAsync(client, s);
        Assert.Equal(3, groups.Total);
        for (var page = 1; page <= 3; page++)
        {
            var single = (await client.GetFromJsonAsync<PageDto<AssigneeWorkloadDto>>(s.DashboardPath + $"/workload?page={page}&pageSize=1"))!;
            Assert.Equal(3, single.Total); Assert.Equal(page, single.Page); Assert.Equal(1, single.PageSize);
            Assert.Equal(groups.Items[page - 1].AssigneeUserId, Assert.Single(single.Items).AssigneeUserId);
        }
        foreach (var suffix in new[] { "/workload", "/overdue" })
        {
            using var response = await client.GetAsync(s.DashboardPath + suffix + "?page=1000000&pageSize=100");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(3, json.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());
        }
    }

    [PostgresTheory]
    [InlineData("/workload", "page=0", "page")]
    [InlineData("/workload", "pageSize=101", "pageSize")]
    [InlineData("/overdue", "page=1000001", "page")]
    [InlineData("/overdue", "pageSize=0", "pageSize")]
    public async Task Invalid_pagination_returns_field_specific_problem_details(string suffix, string query, string field)
    {
        var s = await SetupAsync();
        using var client = fixture.Client(s.Owner);
        using var response = await client.GetAsync(s.DashboardPath + suffix + "?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }

    [PostgresTheory]
    [InlineData("/workload?page=abc")]
    [InlineData("/overdue?pageSize=invalid")]
    public async Task Malformed_pagination_is_rejected_by_http_binding(string suffix)
    {
        var s = await SetupAsync();
        using var client = fixture.Client(s.Owner);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(s.DashboardPath + suffix)).StatusCode);
    }

    [PostgresFact]
    public async Task Dashboard_operations_are_exposed_in_development_openapi()
    {
        using var client = fixture.Client();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        foreach (var suffix in Suffixes)
        {
            var path = "/api/v1/workspaces/{workspaceId}/projects/{projectId}/dashboard" + suffix;
            Assert.True(paths.TryGetProperty(path, out var operation), path);
            Assert.True(operation.TryGetProperty("get", out _));
        }
    }

    private async Task<Scope> SetupAsync(WorkspaceRole role = WorkspaceRole.Developer, bool assigned = true)
    {
        var owner = await fixture.UserAsync();
        var member = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (member, role));
        using var client = fixture.Client(owner);
        var project = await ProjectAsync(client, workspace.Id, "DASH");
        if (assigned)
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
                $"/api/v1/workspaces/{workspace.Id}/projects/{project.Id}/members", new ProjectMemberRequestDto(member.Id))).StatusCode);
        return new Scope(owner, member, workspace, project);
    }

    private static async Task<ProjectResponseDto> ProjectAsync(HttpClient client, Guid workspaceId, string key)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/projects/", new CreateProjectRequestDto("Dashboard test", key));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProjectResponseDto>())!;
    }

    private static async Task<WorkItemDto> ItemAsync(HttpClient client, Scope s, string title, string status = "Todo",
        string priority = "Medium", Guid? assignee = null, int? estimate = null, DateOnly? due = null,
        Guid? parent = null, string type = "Task")
    {
        var response = await client.PostAsJsonAsync(s.ProjectPath + "/work-items",
            new WorkItemWriteDto(title, null, type, status, priority, assignee, parent, null, due, estimate, [], null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WorkItemDto>())!;
    }

    private static async Task<ProjectDashboardDto> DashboardAsync(HttpClient client, Scope s) =>
        (await client.GetFromJsonAsync<ProjectDashboardDto>(s.DashboardPath))!;
    private static async Task<PageDto<AssigneeWorkloadDto>> WorkloadAsync(HttpClient client, Scope s) =>
        (await client.GetFromJsonAsync<PageDto<AssigneeWorkloadDto>>(s.DashboardPath + "/workload"))!;
    private static async Task<PageDto<OverdueWorkItemDto>> OverdueAsync(HttpClient client, Scope s) =>
        (await client.GetFromJsonAsync<PageDto<OverdueWorkItemDto>>(s.DashboardPath + "/overdue"))!;

    private sealed record Scope(User Owner, User Member, Workspace Workspace, ProjectResponseDto Project)
    {
        public string ProjectPath => $"/api/v1/workspaces/{Workspace.Id}/projects/{Project.Id}";
        public string DashboardPath => ProjectPath + "/dashboard";
    }
}
