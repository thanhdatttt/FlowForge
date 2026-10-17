using FlowForge.Api.Contracts.WorkItems;
using FlowForge.Api.Validations;
using FlowForge.Api.Validations.WorkItems;
using FlowForge.Domain.Projects;
using FlowForge.Domain.WorkItems;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Projects;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Api;

public static class CollaborationEndpoints
{
    public static IEndpointRouteBuilder MapCollaborationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}/projects/{projectId:guid}/work-items/{itemId:guid}")
            .WithTags("Collaboration").RequireAuthorization().AddEndpointFilter<WorkspaceMutationFilter>();
        group.MapGet("/comments", async (Guid workspaceId, Guid projectId, Guid itemId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db,
            int? page, int? pageSize, CancellationToken ct) =>
        {
            await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            await WorkItemSupport.ItemAsync(db, projectId, itemId, ct);
            var errors = PaginationValidator.Validate(page, pageSize);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var (number, size) = PaginationValidator.Defaults(page, pageSize);
            var query = db.WorkComments.AsNoTracking().Where(x => x.WorkItemId == itemId && x.DeletedAt == null);
            var total = await query.CountAsync(ct);
            var rows = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip((number - 1) * size).Take(size).ToListAsync(ct);
            var items = new List<CommentDto>();
            foreach (var row in rows) items.Add(await CommentDtoAsync(db, row, ct));
            return Results.Ok(new PageDto<CommentDto>(items, total, number, size));
        });
        group.MapPost("/comments", async (Guid workspaceId, Guid projectId, Guid itemId, CommentWriteDto request,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            WorkItemSupport.ItemWritable(await WorkItemSupport.ItemAsync(db, projectId, itemId, ct));
            var errors = CommentWriteRequestValidator.Validate(request, false);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var mentions = await WorkItemSupport.MentionsAsync(db, workspaceId, projectId, request.Body!, ct);
            var comment = new WorkComment { WorkItemId = itemId, AuthorUserId = ApiRules.UserId(http), Body = request.Body!.Trim() };
            db.WorkComments.Add(comment);
            foreach (var id in mentions) db.CommentMentions.Add(new CommentMention { CommentId = comment.Id, MentionedUserId = id });
            WorkItemSupport.Activity(db, workspaceId, projectId, comment.AuthorUserId, "comment.created", "Comment", comment.Id,
                after: new { comment.WorkItemId, comment.Body, Mentions = mentions }, workItemId: itemId);
            WorkItemSupport.EmitEvent(db, workspaceId, projectId, comment.AuthorUserId, "CommentAdded", comment.Id,
                new { CommentId = comment.Id, WorkItemId = itemId, MentionedUserIds = mentions });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/comments/{comment.Id}",
                await CommentDtoAsync(db, comment, ct));
        });
        group.MapPut("/comments/{commentId:guid}", async (Guid workspaceId, Guid projectId, Guid itemId, Guid commentId,
            CommentWriteDto request, HttpContext http, WorkspaceAccessService access, ProjectAccessService projects,
            FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            WorkItemSupport.ItemWritable(await WorkItemSupport.ItemAsync(db, projectId, itemId, ct));
            var comment = await CommentAsync(db, itemId, commentId, ct);
            RequireAuthorOrModerator(comment.AuthorUserId, ApiRules.UserId(http), role);
            var errors = CommentWriteRequestValidator.Validate(request, true);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            ApiRules.Require(request.Version == comment.Version, 409, "The comment changed. Reload and retry.");
            var oldBody = comment.Body;
            var mentions = await WorkItemSupport.MentionsAsync(db, workspaceId, projectId, request.Body!, ct);
            comment.Body = request.Body!.Trim(); comment.UpdatedAt = DateTimeOffset.UtcNow; comment.Version++;
            var oldMentions = await db.CommentMentions.Where(x => x.CommentId == commentId).ToListAsync(ct);
            db.CommentMentions.RemoveRange(oldMentions.Where(x => !mentions.Contains(x.MentionedUserId)));
            foreach (var id in mentions.Except(oldMentions.Select(x => x.MentionedUserId)))
                db.CommentMentions.Add(new CommentMention { CommentId = commentId, MentionedUserId = id });
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "comment.updated", "Comment", commentId,
                before: new { Body = oldBody }, after: new { comment.Body, Mentions = mentions }, workItemId: itemId);
            WorkItemSupport.EmitEvent(db, workspaceId, projectId, ApiRules.UserId(http), "CommentUpdated", comment.Id,
                new { CommentId = comment.Id, WorkItemId = itemId,
                    NewMentionedUserIds = mentions.Except(oldMentions.Select(x => x.MentionedUserId)).ToArray() });
            await db.SaveChangesAsync(ct);
            return Results.Ok(await CommentDtoAsync(db, comment, ct));
        });
        group.MapDelete("/comments/{commentId:guid}", async (Guid workspaceId, Guid projectId, Guid itemId, Guid commentId,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            WorkItemSupport.ItemWritable(await WorkItemSupport.ItemAsync(db, projectId, itemId, ct));
            var comment = await CommentAsync(db, itemId, commentId, ct);
            RequireAuthorOrModerator(comment.AuthorUserId, ApiRules.UserId(http), role);
            comment.DeletedAt = DateTimeOffset.UtcNow; comment.UpdatedAt = comment.DeletedAt.Value; comment.Version++;
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "comment.deleted", "Comment", commentId,
                before: new { comment.Body }, workItemId: itemId);
            WorkItemSupport.EmitEvent(db, workspaceId, projectId, ApiRules.UserId(http), "CommentDeleted", comment.Id,
                new { CommentId = comment.Id, WorkItemId = itemId });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        group.MapGet("/time-entries", async (Guid workspaceId, Guid projectId, Guid itemId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db,
            int? page, int? pageSize, CancellationToken ct) =>
        {
            await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            await WorkItemSupport.ItemAsync(db, projectId, itemId, ct);
            var errors = PaginationValidator.Validate(page, pageSize);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var (number, size) = PaginationValidator.Defaults(page, pageSize);
            var query = db.TimeEntries.AsNoTracking().Where(x => x.WorkItemId == itemId && x.DeletedAt == null);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((number - 1) * size).Take(size)
                .Select(x => new TimeEntryDto(x.Id, x.UserId, x.Minutes, x.StartedAt, x.Note, x.CreatedAt, x.UpdatedAt)).ToListAsync(ct);
            return Results.Ok(new PageDto<TimeEntryDto>(items, total, number, size));
        });
        group.MapPost("/time-entries", async (Guid workspaceId, Guid projectId, Guid itemId, TimeEntryWriteDto request,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            WorkItemSupport.ItemWritable(await WorkItemSupport.ItemAsync(db, projectId, itemId, ct));
            var errors = TimeEntryWriteRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var entry = new TimeEntry { WorkItemId = itemId, UserId = ApiRules.UserId(http),
                Minutes = request.Minutes, StartedAt = request.StartedAt?.ToUniversalTime(), Note = request.Note?.Trim() };
            db.TimeEntries.Add(entry);
            WorkItemSupport.Activity(db, workspaceId, projectId, entry.UserId, "time-entry.created", "TimeEntry", entry.Id,
                after: new { entry.WorkItemId, entry.Minutes, entry.StartedAt, entry.Note }, workItemId: itemId);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/time-entries/{entry.Id}",
                ToTimeDto(entry));
        });
        group.MapPut("/time-entries/{entryId:guid}", async (Guid workspaceId, Guid projectId, Guid itemId, Guid entryId,
            TimeEntryWriteDto request, HttpContext http, WorkspaceAccessService access, ProjectAccessService projects,
            FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            WorkItemSupport.ItemWritable(await WorkItemSupport.ItemAsync(db, projectId, itemId, ct));
            var entry = await TimeAsync(db, itemId, entryId, ct);
            RequireAuthorOrModerator(entry.UserId, ApiRules.UserId(http), role);
            var errors = TimeEntryWriteRequestValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var before = new { entry.Minutes, entry.StartedAt, entry.Note };
            entry.Minutes = request.Minutes; entry.StartedAt = request.StartedAt?.ToUniversalTime();
            entry.Note = request.Note?.Trim(); entry.UpdatedAt = DateTimeOffset.UtcNow;
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "time-entry.updated", "TimeEntry", entryId,
                before, new { entry.Minutes, entry.StartedAt, entry.Note }, workItemId: itemId);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToTimeDto(entry));
        });
        group.MapDelete("/time-entries/{entryId:guid}", async (Guid workspaceId, Guid projectId, Guid itemId, Guid entryId,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            WorkItemSupport.ItemWritable(await WorkItemSupport.ItemAsync(db, projectId, itemId, ct));
            var entry = await TimeAsync(db, itemId, entryId, ct);
            RequireAuthorOrModerator(entry.UserId, ApiRules.UserId(http), role);
            entry.DeletedAt = DateTimeOffset.UtcNow; entry.UpdatedAt = entry.DeletedAt.Value;
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "time-entry.deleted", "TimeEntry", entryId,
                before: new { entry.Minutes, entry.StartedAt, entry.Note }, workItemId: itemId);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        return app;
    }
    private static async Task<WorkComment> CommentAsync(FlowForgeDbContext db, Guid itemId, Guid commentId, CancellationToken ct)
    {
        var comment = await db.WorkComments.SingleOrDefaultAsync(x => x.WorkItemId == itemId && x.Id == commentId && x.DeletedAt == null, ct);
        ApiRules.Require(comment != null, 404, "Resource not found.");
        return comment!;
    }
    private static async Task<TimeEntry> TimeAsync(FlowForgeDbContext db, Guid itemId, Guid entryId, CancellationToken ct)
    {
        var entry = await db.TimeEntries.SingleOrDefaultAsync(x => x.WorkItemId == itemId && x.Id == entryId && x.DeletedAt == null, ct);
        ApiRules.Require(entry != null, 404, "Resource not found.");
        return entry!;
    }
    private static async Task<CommentDto> CommentDtoAsync(FlowForgeDbContext db, WorkComment comment, CancellationToken ct) =>
        new(comment.Id, comment.AuthorUserId, comment.Body,
            await db.CommentMentions.Where(x => x.CommentId == comment.Id).Select(x => x.MentionedUserId).ToListAsync(ct),
            comment.CreatedAt, comment.UpdatedAt, comment.Version);
    private static TimeEntryDto ToTimeDto(TimeEntry x) => new(x.Id, x.UserId, x.Minutes, x.StartedAt, x.Note, x.CreatedAt, x.UpdatedAt);
    private static void RequireAuthorOrModerator(Guid author, Guid actor, Domain.Workspaces.WorkspaceRole role) =>
        ApiRules.Require(author == actor || ProjectPermissions.CanManage(role, true), 403, "Only the author or a project manager can change this resource.");
}
