using System.IdentityModel.Tokens.Jwt;
using FlowForge.Api.Contracts.Workspaces;
using FlowForge.Api.Validation.Workspaces;
using FlowForge.Domain.Workspaces;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Identity;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FlowForge.Api;

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces").WithTags("Workspaces").RequireAuthorization().AddEndpointFilter<WorkspaceMutationFilter>();

        group.MapPost("/", async (CreateWorkspaceRequestDto request, HttpContext context,
            FlowForgeDbContext db, CancellationToken cancellationToken) =>
        {
            var errors = CreateWorkspaceRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var userId = UserId(context);
            if (!await db.Users.AnyAsync(user => user.Id == userId && user.IsActive, cancellationToken))
                return Results.Unauthorized();

            var workspace = new Workspace { Name = request.Name!.Trim(), CreatedByUserId = userId };
            db.Workspaces.Add(workspace);
            db.WorkspaceMemberships.Add(new WorkspaceMembership
            {
                WorkspaceId = workspace.Id,
                UserId = userId,
                Role = WorkspaceRole.Owner
            });
            WorkItemSupport.Activity(db, workspace.Id, null, userId, "workspace.created", "Workspace", workspace.Id,
                after: new { workspace.Name });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/v1/workspaces/{workspace.Id}",
                new WorkspaceResponseDto(workspace.Id, workspace.Name, WorkspaceRole.Owner.ToString()));
        }).Produces<WorkspaceResponseDto>(StatusCodes.Status201Created).ProducesValidationProblem();

        group.MapGet("/", async (HttpContext context, FlowForgeDbContext db, CancellationToken cancellationToken) =>
        {
            var userId = UserId(context);
            var memberships = await db.WorkspaceMemberships.AsNoTracking()
                .Where(membership => membership.UserId == userId && membership.RemovedAt == null && membership.User.IsActive)
                .OrderBy(membership => membership.Workspace.Name)
                .Select(membership => new { membership.WorkspaceId, membership.Workspace.Name, membership.Role })
                .ToListAsync(cancellationToken);
            return Results.Ok(memberships.Select(membership =>
                new WorkspaceResponseDto(membership.WorkspaceId, membership.Name, membership.Role.ToString())));
        }).Produces<IEnumerable<WorkspaceResponseDto>>();

        group.MapGet("/{workspaceId:guid}", async (Guid workspaceId, HttpContext context,
            WorkspaceAccessService access, FlowForgeDbContext db, CancellationToken cancellationToken) =>
        {
            var role = await access.GetRoleAsync(workspaceId, UserId(context), cancellationToken);
            if (role is null) return Results.NotFound();
            var workspace = await db.Workspaces.AsNoTracking().SingleAsync(item => item.Id == workspaceId, cancellationToken);
            return Results.Ok(new WorkspaceResponseDto(workspace.Id, workspace.Name, role.Value.ToString()));
        }).Produces<WorkspaceResponseDto>().Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{workspaceId:guid}", async (Guid workspaceId, UpdateWorkspaceRequestDto request,
            HttpContext context, WorkspaceAccessService access, FlowForgeDbContext db, CancellationToken cancellationToken) =>
        {
            var role = await access.GetRoleAsync(workspaceId, UserId(context), cancellationToken);
            if (role is null) return Results.NotFound();
            if (!WorkspacePermissions.Allows(role.Value, WorkspacePermission.ManageWorkspace)) return Results.Forbid();
            var errors = UpdateWorkspaceRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var workspace = await db.Workspaces.SingleAsync(item => item.Id == workspaceId, cancellationToken);
            var oldName = workspace.Name;
            workspace.Name = request.Name!.Trim();
            workspace.UpdatedAt = DateTimeOffset.UtcNow;
            WorkItemSupport.Activity(db, workspaceId, null, UserId(context), "workspace.updated", "Workspace", workspaceId,
                before: new { Name = oldName }, after: new { workspace.Name });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new WorkspaceResponseDto(workspace.Id, workspace.Name, role.Value.ToString()));
        }).Produces<WorkspaceResponseDto>().ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{workspaceId:guid}/members", async (Guid workspaceId, HttpContext context,
            WorkspaceAccessService access, FlowForgeDbContext db, CancellationToken cancellationToken) =>
        {
            var role = await access.GetRoleAsync(workspaceId, UserId(context), cancellationToken);
            if (role is null) return Results.NotFound();
            var members = await db.WorkspaceMemberships.AsNoTracking()
                .Where(membership => membership.WorkspaceId == workspaceId && membership.RemovedAt == null)
                .OrderBy(membership => membership.User.DisplayName)
                .Select(membership => new { membership.UserId, membership.User.Email,
                    membership.User.DisplayName, membership.Role })
                .ToListAsync(cancellationToken);
            return Results.Ok(members.Select(member =>
                new WorkspaceMemberResponseDto(member.UserId, member.Email, member.DisplayName, member.Role.ToString())));
        }).Produces<IEnumerable<WorkspaceMemberResponseDto>>().Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{workspaceId:guid}/members", async (Guid workspaceId, AddWorkspaceMemberRequestDto request,
            HttpContext context, WorkspaceAccessService access, FlowForgeDbContext db, CancellationToken cancellationToken) =>
        {
            var actorRole = await access.GetRoleAsync(workspaceId, UserId(context), cancellationToken);
            if (actorRole is null) return Results.NotFound();
            if (!WorkspacePermissions.Allows(actorRole.Value, WorkspacePermission.ManageMembers)) return Results.Forbid();
            var errors = AddWorkspaceMemberRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var newRole = Enum.Parse<WorkspaceRole>(request.Role!, true);
            if (!WorkspacePermissions.CanAssignRole(actorRole.Value, newRole)) return Results.Forbid();

            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.NormalizedEmail == AuthService.NormalizeEmail(request.Email!) && candidate.IsActive,
                cancellationToken);
            if (user is null) return Results.NotFound();
            if (await db.WorkspaceMemberships.AnyAsync(membership => membership.WorkspaceId == workspaceId &&
                membership.UserId == user.Id && membership.RemovedAt == null, cancellationToken))
                return Results.Conflict();

            db.WorkspaceMemberships.Add(new WorkspaceMembership
            {
                WorkspaceId = workspaceId,
                UserId = user.Id,
                Role = newRole
            });
            WorkItemSupport.Activity(db, workspaceId, null, UserId(context), "workspace-member.added", "WorkspaceMember", user.Id,
                after: new { Role = newRole.ToString() });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Results.Conflict();
            }
            return Results.Created($"/api/v1/workspaces/{workspaceId}/members/{user.Id}",
                new WorkspaceMemberResponseDto(user.Id, user.Email, user.DisplayName, newRole.ToString()));
        }).Produces<WorkspaceMemberResponseDto>(StatusCodes.Status201Created).ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPut("/{workspaceId:guid}/members/{userId:guid}/role", async (Guid workspaceId, Guid userId,
            ChangeWorkspaceRoleRequestDto request, HttpContext context, WorkspaceAccessService access,
            FlowForgeDbContext db, CancellationToken cancellationToken) =>
        {
            var actorRole = await access.GetRoleAsync(workspaceId, UserId(context), cancellationToken);
            if (actorRole is null) return Results.NotFound();
            if (!WorkspacePermissions.Allows(actorRole.Value, WorkspacePermission.ManageMembers)) return Results.Forbid();
            var errors = ChangeWorkspaceRoleRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var newRole = Enum.Parse<WorkspaceRole>(request.Role!, true);
            var membership = await db.WorkspaceMemberships.Include(item => item.User)
                .SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.UserId == userId &&
                    item.RemovedAt == null, cancellationToken);
            if (membership is null) return Results.NotFound();
            if (!WorkspacePermissions.CanManageRole(actorRole.Value, membership.Role) ||
                !WorkspacePermissions.CanAssignRole(actorRole.Value, newRole)) return Results.Forbid();

            var oldRole = membership.Role;
            membership.Role = newRole;
            WorkItemSupport.Activity(db, workspaceId, null, UserId(context), "workspace-member.role-changed", "WorkspaceMember", userId,
                before: new { Role = oldRole.ToString() }, after: new { Role = newRole.ToString() });
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new WorkspaceMemberResponseDto(userId, membership.User.Email,
                membership.User.DisplayName, newRole.ToString()));
        }).Produces<WorkspaceMemberResponseDto>().ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{workspaceId:guid}/members/{userId:guid}", async (Guid workspaceId, Guid userId,
            HttpContext context, WorkspaceAccessService access, FlowForgeDbContext db, CancellationToken cancellationToken) =>
        {
            var actorRole = await access.GetRoleAsync(workspaceId, UserId(context), cancellationToken);
            if (actorRole is null) return Results.NotFound();
            if (!WorkspacePermissions.Allows(actorRole.Value, WorkspacePermission.ManageMembers)) return Results.Forbid();
            var membership = await db.WorkspaceMemberships.SingleOrDefaultAsync(item =>
                item.WorkspaceId == workspaceId && item.UserId == userId && item.RemovedAt == null,
                cancellationToken);
            if (membership is null) return Results.NotFound();
            if (!WorkspacePermissions.CanManageRole(actorRole.Value, membership.Role)) return Results.Forbid();

            var now = DateTimeOffset.UtcNow;
            await db.ProjectMemberships.Where(item => item.WorkspaceMembershipId == membership.Id && item.RemovedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RemovedAt, now), cancellationToken);
            await db.Projects.Where(item => item.LeadMembershipId == membership.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LeadMembershipId, (Guid?)null)
                    .SetProperty(item => item.UpdatedAt, now).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken);
            membership.RemovedAt = now;
            WorkItemSupport.Activity(db, workspaceId, null, UserId(context), "workspace-member.removed", "WorkspaceMember", userId,
                before: new { Role = membership.Role.ToString() });
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{workspaceId:guid}/ownership/transfer", async (Guid workspaceId,
            TransferOwnershipRequestDto request, HttpContext http, WorkspaceAccessService access,
            FlowForgeDbContext db, CancellationToken ct) =>
        {
            var role = await ApiRules.RoleAsync(workspaceId, http, access, ct);
            ApiRules.Require(role == WorkspaceRole.Owner, 403, "Only the current Owner may transfer ownership.");
            var errors = TransferOwnershipRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var actorId = ApiRules.UserId(http);
            ApiRules.Require(request.NewOwnerUserId != actorId, 400, "Choose another active member.");
            var target = await db.WorkspaceMemberships.Include(x => x.User).SingleOrDefaultAsync(x =>
                x.WorkspaceId == workspaceId && x.UserId == request.NewOwnerUserId && x.RemovedAt == null, ct);
            ApiRules.Require(target != null && target.User.IsActive && target.User.EmailVerifiedAt != null,
                400, "The new Owner must be an active member with a verified email.");
            var owner = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == workspaceId &&
                x.UserId == actorId && x.RemovedAt == null, ct);
            // The enclosing filter supplies the transaction and workspace lock. Two saves
            // avoid the immediate unique-owner index; the deferred trigger checks existence.
            owner.Role = WorkspaceRole.Admin;
            await db.SaveChangesAsync(ct);
            target!.Role = WorkspaceRole.Owner;
            WorkItemSupport.Activity(db, workspaceId, null, actorId, "workspace.ownership-transferred", "Workspace", workspaceId,
                after: new { NewOwnerUserId = target.UserId });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Produces(204).ProducesProblem(400).ProducesProblem(403);

        return app;
    }

    private static Guid UserId(HttpContext context) => Guid.Parse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
