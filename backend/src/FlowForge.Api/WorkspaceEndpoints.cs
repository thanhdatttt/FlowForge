using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using FlowForge.Api.Contracts.Workspaces;
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
        var group = app.MapGroup("/api/v1/workspaces").WithTags("Workspaces").RequireAuthorization();

        group.MapPost("/", async (CreateWorkspaceRequestDto request, HttpContext context,
            FlowForgeDbContext db, CancellationToken cancellationToken) =>
        {
            if (!ValidName(request.Name)) return Results.ValidationProblem(NameError());
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
            if (!ValidName(request.Name)) return Results.ValidationProblem(NameError());
            var workspace = await db.Workspaces.SingleAsync(item => item.Id == workspaceId, cancellationToken);
            workspace.Name = request.Name!.Trim();
            workspace.UpdatedAt = DateTimeOffset.UtcNow;
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
            if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 320 ||
                !new EmailAddressAttribute().IsValid(request.Email.Trim()) || !TryRole(request.Role, out var newRole))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["member"] = ["Valid email and role are required."] });
            if (!WorkspacePermissions.CanAssignRole(actorRole.Value, newRole)) return Results.Forbid();

            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(candidate =>
                candidate.NormalizedEmail == AuthService.NormalizeEmail(request.Email) && candidate.IsActive,
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
            if (!TryRole(request.Role, out var newRole))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["A valid role is required."] });
            var membership = await db.WorkspaceMemberships.Include(item => item.User)
                .SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.UserId == userId &&
                    item.RemovedAt == null, cancellationToken);
            if (membership is null) return Results.NotFound();
            if (!WorkspacePermissions.CanManageRole(actorRole.Value, membership.Role) ||
                !WorkspacePermissions.CanAssignRole(actorRole.Value, newRole)) return Results.Forbid();

            membership.Role = newRole;
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

            membership.RemovedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }).Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static Guid UserId(HttpContext context) => Guid.Parse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
    private static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100;
    private static Dictionary<string, string[]> NameError() => new() { ["name"] = ["Name must be between 1 and 100 characters."] };
    private static bool TryRole(string? value, out WorkspaceRole role)
    {
        role = default;
        return value is not null && Enum.GetNames<WorkspaceRole>().Any(name =>
            string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) &&
            Enum.TryParse(value, true, out role);
    }
}
