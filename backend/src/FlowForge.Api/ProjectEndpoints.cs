using FlowForge.Api.Contracts.Projects;
using FlowForge.Api.Validations;
using FlowForge.Api.Validations.Projects;
using FlowForge.Domain.Projects;
using FlowForge.Domain.WorkItems;
using FlowForge.Domain.Workspaces;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Projects;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}/projects")
            .WithTags("Projects").RequireAuthorization().AddEndpointFilter<WorkspaceMutationFilter>();
        group.MapPost("/", async (Guid workspaceId, CreateProjectRequestDto request, HttpContext http,
            WorkspaceAccessService access, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var role = await ApiRules.RoleAsync(workspaceId, http, access, ct);
            ApiRules.Require(ProjectPermissions.CanCreate(role), 403, "Only Owners and Admins can create projects.");
            var errors = CreateProjectRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var lead = await LeadAsync(db, workspaceId, request.LeadUserId, ct);
            var project = new Project
            {
                WorkspaceId = workspaceId, Name = request.Name!.Trim(), ProjectKey = request.ProjectKey!.Trim().ToUpperInvariant(),
                Description = request.Description?.Trim(), LeadMembershipId = lead?.Id,
                StartsOn = request.StartsOn, TargetOn = request.TargetOn
            };
            db.Projects.Add(project);
            var defaultStatuses = new[] { ("Todo", "Open"), ("InProgress", "Open"),
                ("InReview", "Open"), ("Done", "Done"), ("Blocked", "Open") };
            for (var i = 0; i < defaultStatuses.Length; i++)
                db.ProjectStatuses.Add(new ProjectStatus { ProjectId = project.Id, Name = defaultStatuses[i].Item1,
                    Category = defaultStatuses[i].Item2, SortOrder = i });
            if (lead != null) AddMembership(db, project, lead.Id);
            WorkItemSupport.Activity(db, workspaceId, project.Id, ApiRules.UserId(http), "project.created", "Project", project.Id,
                after: new { project.Name, project.ProjectKey, project.Description, project.LeadMembershipId });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/workspaces/{workspaceId}/projects/{project.Id}", ToResponse(project, lead?.UserId));
        }).Produces<ProjectResponseDto>(201).ProducesValidationProblem().ProducesProblem(403).ProducesProblem(409);

        group.MapGet("/", async (Guid workspaceId, HttpContext http, WorkspaceAccessService access,
            ProjectAccessService projects, FlowForgeDbContext db, string? search, bool? archived, Guid? leadUserId,
            string? sort, bool? descending, int? page, int? pageSize, CancellationToken ct) =>
        {
            var role = await ApiRules.RoleAsync(workspaceId, http, access, ct);
            var errors = ProjectListQueryValidator.Validate(search, sort, leadUserId, page, pageSize);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var (number, size) = PaginationValidator.Defaults(page, pageSize);
            var query = projects.VisibleProjects(workspaceId, ApiRules.UserId(http), role).AsNoTracking();
            if (archived != null) query = query.Where(x => (x.ArchivedAt != null) == archived);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLowerInvariant();
                query = query.Where(x => x.Name.ToLower().Contains(term) || x.ProjectKey.ToLower().Contains(term));
            }
            if (leadUserId != null) query = query.Where(x => x.LeadMembership != null && x.LeadMembership.UserId == leadUserId);
            var total = await query.CountAsync(ct);
            var desc = descending ?? false;
            query = (sort ?? "name") switch
            {
                "name" => desc ? query.OrderByDescending(x => x.Name).ThenBy(x => x.Id) : query.OrderBy(x => x.Name).ThenBy(x => x.Id),
                "key" => desc ? query.OrderByDescending(x => x.ProjectKey).ThenBy(x => x.Id) : query.OrderBy(x => x.ProjectKey).ThenBy(x => x.Id),
                "createdAt" => desc ? query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id) : query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
                "updatedAt" => desc ? query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id) : query.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id),
                _ => throw new InvalidOperationException("Sort must be validated before querying.")
            };
            var items = await query.Skip((number - 1) * size).Take(size)
                .Select(x => new ProjectResponseDto(x.Id, x.WorkspaceId, x.ProjectKey, x.Name, x.Description,
                    x.LeadMembership == null ? null : x.LeadMembership.UserId, x.StartsOn, x.TargetOn,
                    x.ArchivedAt, x.CreatedAt, x.UpdatedAt, x.Version)).ToListAsync(ct);
            return Results.Ok(new ProjectPageDto(items, total, number, size));
        }).Produces<ProjectPageDto>().ProducesValidationProblem();

        group.MapGet("/{projectId:guid}", async (Guid workspaceId, Guid projectId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, CancellationToken ct) =>
        {
            var (project, _) = await FindAsync(workspaceId, projectId, http, access, projects, ct);
            return Results.Ok(ToResponse(project, project.LeadMembership?.UserId));
        }).Produces<ProjectResponseDto>();

        group.MapPut("/{projectId:guid}", async (Guid workspaceId, Guid projectId, UpdateProjectRequestDto request,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await FindAsync(workspaceId, projectId, http, access, projects, ct);
            RequireManage(role);
            RequireWritable(project);
            var errors = UpdateProjectRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            ApiRules.Require(request.Version == project.Version, 409, "The project changed. Reload and retry.");
            if (!string.Equals(project.ProjectKey, request.ProjectKey!.Trim(), StringComparison.OrdinalIgnoreCase))
                ApiRules.Require(!await db.WorkItems.AnyAsync(x => x.ProjectId == projectId, ct), 409,
                    "Project key cannot change after work items exist.");
            var lead = await LeadAsync(db, workspaceId, request.LeadUserId, ct);
            var before = new { project.Name, project.ProjectKey, project.Description, project.LeadMembershipId,
                project.StartsOn, project.TargetOn };
            project.Name = request.Name!.Trim();
            project.ProjectKey = request.ProjectKey!.Trim().ToUpperInvariant();
            project.Description = request.Description?.Trim();
            project.StartsOn = request.StartsOn;
            project.TargetOn = request.TargetOn;
            project.LeadMembershipId = lead?.Id;
            if (lead != null && !await db.ProjectMemberships.AnyAsync(x => x.ProjectId == projectId &&
                x.WorkspaceMembershipId == lead.Id && x.RemovedAt == null, ct)) AddMembership(db, project, lead.Id);
            Touch(project);
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "project.updated", "Project", projectId,
                before, new { project.Name, project.ProjectKey, project.Description, project.LeadMembershipId,
                    project.StartsOn, project.TargetOn });
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToResponse(project, lead?.UserId));
        }).Produces<ProjectResponseDto>().ProducesValidationProblem().ProducesProblem(409);

        group.MapPost("/{projectId:guid}/archive", (Guid workspaceId, Guid projectId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
            ArchiveAsync(workspaceId, projectId, http, access, projects, db, true, ct));
        group.MapPost("/{projectId:guid}/restore", (Guid workspaceId, Guid projectId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
            ArchiveAsync(workspaceId, projectId, http, access, projects, db, false, ct));

        group.MapGet("/{projectId:guid}/members", async (Guid workspaceId, Guid projectId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, int? page, int? pageSize, CancellationToken ct) =>
        {
            await FindAsync(workspaceId, projectId, http, access, projects, ct);
            var errors = PaginationValidator.Validate(page, pageSize);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var (number, size) = PaginationValidator.Defaults(page, pageSize);
            var query = db.ProjectMemberships.AsNoTracking().Where(x => x.ProjectId == projectId &&
                x.RemovedAt == null && x.WorkspaceMembership.RemovedAt == null && x.WorkspaceMembership.User.IsActive);
            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(x => x.WorkspaceMembership.User.DisplayName).ThenBy(x => x.Id)
                .Skip((number - 1) * size).Take(size).Select(x => new
                {
                    x.WorkspaceMembership.UserId, x.WorkspaceMembership.User.DisplayName,
                    x.WorkspaceMembership.User.Email, x.WorkspaceMembership.Role
                }).ToListAsync(ct);
            return Results.Ok(new { items = items.Select(x => new ProjectMemberResponseDto(x.UserId, x.DisplayName, x.Email, x.Role.ToString())),
                total, page = number, pageSize = size });
        });
        group.MapPost("/{projectId:guid}/members", async (Guid workspaceId, Guid projectId, ProjectMemberRequestDto request,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await FindAsync(workspaceId, projectId, http, access, projects, ct);
            RequireManage(role);
            RequireWritable(project);
            var errors = ProjectMemberRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var member = await LeadAsync(db, workspaceId, request.UserId, ct);
            ApiRules.Require(!await db.ProjectMemberships.AnyAsync(x => x.ProjectId == projectId &&
                x.WorkspaceMembershipId == member!.Id && x.RemovedAt == null, ct), 409, "The user is already a project member.");
            AddMembership(db, project, member!.Id);
            Touch(project);
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "project-member.added", "Project", projectId,
                after: new { request.UserId });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/workspaces/{workspaceId}/projects/{projectId}/members/{request.UserId}",
                new ProjectMemberResponseDto(member.UserId, member.User.DisplayName, member.User.Email, member.Role.ToString()));
        }).Produces<ProjectMemberResponseDto>(201).ProducesValidationProblem();
        group.MapDelete("/{projectId:guid}/members/{userId:guid}", async (Guid workspaceId, Guid projectId, Guid userId,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await FindAsync(workspaceId, projectId, http, access, projects, ct);
            RequireManage(role);
            RequireWritable(project);
            var member = await db.ProjectMemberships.Include(x => x.WorkspaceMembership).SingleOrDefaultAsync(x =>
                x.ProjectId == projectId && x.RemovedAt == null && x.WorkspaceMembership.UserId == userId &&
                x.WorkspaceMembership.RemovedAt == null, ct);
            ApiRules.Require(member != null, 404, "Resource not found.");
            ApiRules.Require(project.LeadMembershipId != member!.WorkspaceMembershipId, 409, "Change or clear the project lead before removing them.");
            member.RemovedAt = DateTimeOffset.UtcNow;
            Touch(project);
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "project-member.removed", "Project", projectId,
                before: new { UserId = userId });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Produces(204);
        return app;
    }

    private static async Task<IResult> ArchiveAsync(Guid workspaceId, Guid projectId, HttpContext http,
        WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, bool archive, CancellationToken ct)
    {
        var (project, role) = await FindAsync(workspaceId, projectId, http, access, projects, ct);
        RequireManage(role);
        if ((project.ArchivedAt != null) != archive)
        {
            project.ArchivedAt = archive ? DateTimeOffset.UtcNow : null;
            Touch(project);
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http),
                archive ? "project.archived" : "project.restored", "Project", projectId);
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(ToResponse(project, project.LeadMembership?.UserId));
    }
    private static async Task<(Project Project, WorkspaceRole Role)> FindAsync(Guid workspaceId, Guid projectId,
        HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, CancellationToken ct)
    {
        var role = await ApiRules.RoleAsync(workspaceId, http, access, ct);
        var project = await projects.VisibleProjects(workspaceId, ApiRules.UserId(http), role)
            .Include(x => x.LeadMembership).SingleOrDefaultAsync(x => x.Id == projectId, ct);
        ApiRules.Require(project != null, 404, "Resource not found.");
        return (project!, role);
    }
    private static void RequireManage(WorkspaceRole role) =>
        ApiRules.Require(ProjectPermissions.CanManage(role, true), 403, "You cannot manage this project.");
    private static void RequireWritable(Project project) =>
        ApiRules.Require(project.ArchivedAt == null, 409, "Archived projects are read-only. Restore the project first.");
    private static async Task<WorkspaceMembership?> LeadAsync(FlowForgeDbContext db, Guid workspaceId, Guid? userId, CancellationToken ct)
    {
        if (userId == null) return null;
        var member = await db.WorkspaceMemberships.Include(x => x.User).SingleOrDefaultAsync(x =>
            x.WorkspaceId == workspaceId && x.UserId == userId && x.RemovedAt == null && x.User.IsActive, ct);
        ApiRules.Require(member != null, 400, "The referenced user must be an active member of this workspace.");
        return member;
    }
    private static void AddMembership(FlowForgeDbContext db, Project project, Guid membershipId) =>
        db.ProjectMemberships.Add(new ProjectMembership { WorkspaceId = project.WorkspaceId,
            ProjectId = project.Id, WorkspaceMembershipId = membershipId });
    private static void Touch(Project project) { project.Version++; project.UpdatedAt = DateTimeOffset.UtcNow; }
    private static ProjectResponseDto ToResponse(Project x, Guid? leadUserId) => new(x.Id, x.WorkspaceId,
        x.ProjectKey, x.Name, x.Description, leadUserId, x.StartsOn, x.TargetOn, x.ArchivedAt, x.CreatedAt, x.UpdatedAt, x.Version);
}
