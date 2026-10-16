using FlowForge.Api.Contracts.Workspaces;
using FlowForge.Api.Validation;
using FlowForge.Api.Validation.Workspaces;
using FlowForge.Domain.Workspaces;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Identity;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}/invitations")
            .WithTags("Invitations").RequireAuthorization().AddEndpointFilter<WorkspaceMutationFilter>();
        group.MapPost("/", async (Guid workspaceId, CreateInvitationRequestDto request, HttpContext http,
            WorkspaceAccessService access, FlowForgeDbContext db, IConfiguration configuration, CancellationToken ct) =>
        {
            var actorRole = await ApiRules.RoleAsync(workspaceId, http, access, ct);
            ApiRules.Require(WorkspacePermissions.Allows(actorRole, WorkspacePermission.ManageMembers), 403, "Permission denied.");
            var errors = CreateInvitationRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var role = Enum.Parse<WorkspaceRole>(request.Role!, true);
            ApiRules.Require(WorkspacePermissions.CanAssignRole(actorRole, role), 403, "You cannot assign this role.");
            var email = AuthService.NormalizeEmail(request.Email!);
            ApiRules.Require(!await db.WorkspaceMemberships.AnyAsync(x => x.WorkspaceId == workspaceId &&
                x.RemovedAt == null && x.User.NormalizedEmail == email, ct), 409, "The user is already a member.");
            var now = DateTimeOffset.UtcNow;
            var previous = await db.WorkspaceInvitations.Where(x => x.WorkspaceId == workspaceId &&
                x.NormalizedEmail == email && x.AcceptedAt == null && x.RevokedAt == null).ToListAsync(ct);
            ApiRules.Require(!previous.Any(x => x.ExpiresAt > now), 409, "A pending invitation already exists.");
            // Release the pending-invitation unique key without changing its Expired state.
            if (previous.Count > 0)
            {
                foreach (var expired in previous) expired.RevokedAt = now;
                await db.SaveChangesAsync(ct);
            }
            var days = configuration.GetValue<int?>("Invitations:LifetimeDays") ?? 7;
            ApiRules.Require(days is >= 1 and <= 30, 503, "Invitation lifetime is invalid.");
            var raw = SecureTokens.Generate();
            var invitation = new WorkspaceInvitation
            {
                WorkspaceId = workspaceId, NormalizedEmail = email, Role = role,
                TokenHash = SecureTokens.Hash(raw), InvitedByUserId = ApiRules.UserId(http),
                CreatedAt = now, ExpiresAt = now.AddDays(days)
            };
            db.WorkspaceInvitations.Add(invitation);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/workspaces/{workspaceId}/invitations/{invitation.Id}",
                new CreatedInvitationResponseDto(ToResponse(invitation), raw, InvitationUrl(configuration, invitation.Id, raw)));
        }).Produces<CreatedInvitationResponseDto>(201).ProducesValidationProblem().ProducesProblem(403).ProducesProblem(409);

        group.MapGet("/", async (Guid workspaceId, HttpContext http, WorkspaceAccessService access,
            FlowForgeDbContext db, int? page, int? pageSize, CancellationToken ct) =>
        {
            var role = await ApiRules.RoleAsync(workspaceId, http, access, ct);
            ApiRules.Require(WorkspacePermissions.Allows(role, WorkspacePermission.ManageMembers), 403, "Permission denied.");
            var errors = PaginationValidator.Validate(page, pageSize);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var (number, size) = PaginationValidator.Defaults(page, pageSize);
            var query = db.WorkspaceInvitations.AsNoTracking().Where(x => x.WorkspaceId == workspaceId);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
                .Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return Results.Ok(new { items = items.Select(ToResponse), total, page = number, pageSize = size });
        });
        group.MapPost("/{invitationId:guid}/revoke", async (Guid workspaceId, Guid invitationId, HttpContext http,
            WorkspaceAccessService access, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var role = await ApiRules.RoleAsync(workspaceId, http, access, ct);
            ApiRules.Require(WorkspacePermissions.Allows(role, WorkspacePermission.ManageMembers), 403, "Permission denied.");
            var invitation = await db.WorkspaceInvitations.SingleOrDefaultAsync(x => x.Id == invitationId && x.WorkspaceId == workspaceId, ct);
            ApiRules.Require(invitation != null, 404, "Resource not found.");
            ApiRules.Require(WorkspacePermissions.CanAssignRole(role, invitation!.Role), 403, "You cannot manage this invitation role.");
            ApiRules.Require(invitation.Status(DateTimeOffset.UtcNow) == "Pending", 409, "The invitation is no longer pending.");
            invitation.RevokedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).Produces(204);

        var recipient = app.MapGroup("/api/v1/invitations").WithTags("Invitations").RequireAuthorization();
        recipient.MapGet("/{invitationId:guid}", async (Guid invitationId, HttpContext http,
            FlowForgeDbContext db, CancellationToken ct) =>
        {
            var userId = ApiRules.UserId(http);
            var invitation = await db.WorkspaceInvitations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == invitationId, ct);
            var email = await db.Users.Where(x => x.Id == userId && x.IsActive).Select(x => x.NormalizedEmail).SingleOrDefaultAsync(ct);
            ApiRules.Require(invitation != null && invitation.NormalizedEmail == email, 404, "Invitation not found.");
            return Results.Ok(ToResponse(invitation!));
        }).Produces<InvitationResponseDto>();
        recipient.MapPost("/accept", (InvitationTokenRequestDto request, HttpContext http,
            FlowForgeDbContext db, WorkspaceAccessService access, CancellationToken ct) =>
            ConsumeAsync(request, http, db, access, false, ct));
        recipient.MapPost("/reject", (InvitationTokenRequestDto request, HttpContext http,
            FlowForgeDbContext db, WorkspaceAccessService access, CancellationToken ct) =>
            ConsumeAsync(request, http, db, access, true, ct));
        return app;
    }

    private static async Task<IResult> ConsumeAsync(InvitationTokenRequestDto request, HttpContext http,
        FlowForgeDbContext db, WorkspaceAccessService access, bool reject, CancellationToken ct)
    {
        var errors = InvitationTokenRequestValidator.Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var hash = SecureTokens.Hash(request.Token!);
        var workspaceId = await db.WorkspaceInvitations.AsNoTracking().Where(x => x.TokenHash == hash)
            .Select(x => (Guid?)x.WorkspaceId).SingleOrDefaultAsync(ct);
        ApiRules.Require(workspaceId != null, 404, "Invitation not found.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await WorkspaceLock.AcquireAsync(db, workspaceId!.Value, ct);
        var userId = ApiRules.UserId(http);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        ApiRules.Require(user != null, 401, "Authentication required.");
        var invitation = await db.WorkspaceInvitations.SingleAsync(x => x.TokenHash == hash, ct);
        ApiRules.Require(user!.NormalizedEmail == invitation.NormalizedEmail, 404, "Invitation not found.");
        ApiRules.Require(user.EmailVerifiedAt != null, 403, "Verify your email before responding to an invitation.");
        ApiRules.Require(invitation.Status(DateTimeOffset.UtcNow) == "Pending", 409, "The invitation is no longer pending.");
        if (reject) invitation.RevokedAt = DateTimeOffset.UtcNow;
        else
        {
            var inviterRole = await access.GetRoleAsync(invitation.WorkspaceId, invitation.InvitedByUserId, ct);
            ApiRules.Require(inviterRole != null && WorkspacePermissions.CanAssignRole(inviterRole.Value, invitation.Role),
                409, "The inviter can no longer grant this role. Request a new invitation.");
            ApiRules.Require(!await db.WorkspaceMemberships.AnyAsync(x => x.WorkspaceId == invitation.WorkspaceId &&
                x.UserId == userId && x.RemovedAt == null, ct), 409, "You are already a member.");
            db.WorkspaceMemberships.Add(new WorkspaceMembership
            {
                WorkspaceId = invitation.WorkspaceId, UserId = userId, Role = invitation.Role
            });
            invitation.AcceptedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static string InvitationUrl(IConfiguration configuration, Guid invitationId, string raw)
    {
        var configured = configuration["PublicApiUrl"] ?? "http://localhost:8080";
        ApiRules.Require(Uri.TryCreate(configured, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) &&
            string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo),
            503, "PublicApiUrl must be an HTTPS URL or a local HTTP URL.");
        // Fragments are not sent to the server or included in HTTP request logs.
        return $"{configured.TrimEnd('/')}/api/v1/invitations/{invitationId}#token={raw}";
    }

    private static InvitationResponseDto ToResponse(WorkspaceInvitation x) => new(x.Id, x.WorkspaceId,
        x.NormalizedEmail, x.Role.ToString(), x.Status(DateTimeOffset.UtcNow), x.CreatedAt, x.ExpiresAt);
}
