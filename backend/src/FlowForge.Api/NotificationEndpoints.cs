using FlowForge.Api.Contracts.Notifications;
using FlowForge.Api.Contracts.WorkItems;
using FlowForge.Api.Validations;
using FlowForge.Api.Validations.Notifications;
using FlowForge.Domain.Notifications;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireAuthorization();
        group.MapGet("/", async (HttpContext http, NotificationAccess access, FlowForgeDbContext db,
            int? page, int? pageSize, bool? unreadOnly, CancellationToken ct) =>
        {
            var userId = ApiRules.UserId(http);
            ApiRules.Require(await db.Users.AnyAsync(x => x.Id == userId && x.IsActive, ct), 401, "Authentication required.");
            var errors = PaginationValidator.Validate(page, pageSize);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var (number, size) = PaginationValidator.Defaults(page, pageSize);
            var query = access.Visible(userId).AsNoTracking();
            if (unreadOnly == true) query = query.Where(x => x.ReadAt == null);
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Skip((number - 1) * size).Take(size).ToListAsync(ct);
            return Results.Ok(new PageDto<NotificationDto>(rows.Select(ToDto).ToList(), total, number, size));
        }).Produces<PageDto<NotificationDto>>().ProducesValidationProblem();
        group.MapPost("/{notificationId:guid}/read", async (Guid notificationId, HttpContext http,
            NotificationAccess access, TimeProvider clock, CancellationToken ct) =>
        {
            var errors = NotificationRequestValidator.Validate(notificationId);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var query = access.Visible(ApiRules.UserId(http)).Where(x => x.Id == notificationId);
            ApiRules.Require(await query.AnyAsync(ct), 404, "Resource not found.");
            await query.Where(x => x.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, clock.GetUtcNow()), ct);
            return Results.NoContent();
        }).Produces(204).ProducesValidationProblem().ProducesProblem(404);
        group.MapPost("/read-all", async (HttpContext http, NotificationAccess access, FlowForgeDbContext db,
            TimeProvider clock, CancellationToken ct) =>
        {
            var userId = ApiRules.UserId(http);
            ApiRules.Require(await db.Users.AnyAsync(x => x.Id == userId && x.IsActive, ct), 401, "Authentication required.");
            await access.Visible(userId).Where(x => x.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAt, clock.GetUtcNow()), ct);
            return Results.NoContent();
        }).Produces(204);
        return app;
    }

    private static NotificationDto ToDto(Notification n) => new(n.Id, n.Type, n.WorkspaceId, n.ProjectId,
        n.WorkItemId, n.CommentId, n.InvitationId, n.InvitationId != null
            ? $"/api/v1/invitations/{n.InvitationId}"
            : $"/api/v1/workspaces/{n.WorkspaceId}/projects/{n.ProjectId}/work-items/{n.WorkItemId}", n.CreatedAt, n.ReadAt);
}
