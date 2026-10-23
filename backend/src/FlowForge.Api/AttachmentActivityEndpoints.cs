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

public static class AttachmentActivityEndpoints
{
    public static IEndpointRouteBuilder MapAttachmentActivityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}/projects/{projectId:guid}/work-items/{itemId:guid}/attachments")
            .WithTags("Attachments").RequireAuthorization().AddEndpointFilter<WorkspaceMutationFilter>();
        group.MapGet("/", async (Guid workspaceId, Guid projectId, Guid itemId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db,
            int? page, int? pageSize, CancellationToken ct) =>
        {
            await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            await WorkItemSupport.ItemAsync(db, projectId, itemId, ct);
            var errors = PaginationValidator.Validate(page, pageSize);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var (number, size) = PaginationValidator.Defaults(page, pageSize);
            var query = db.WorkAttachments.AsNoTracking().Where(x => x.WorkItemId == itemId && x.DeletedAt == null);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((number - 1) * size).Take(size)
                .Select(x => new AttachmentDto(x.Id, x.OriginalName, x.ContentType, x.SizeBytes, x.UploadedByUserId, x.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(new PageDto<AttachmentDto>(items, total, number, size));
        });
        group.MapPost("/", async (Guid workspaceId, Guid projectId, Guid itemId, HttpContext http,
            WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db,
            IConfiguration configuration, AttachmentFileActions files, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            WorkItemSupport.ItemWritable(await WorkItemSupport.ItemAsync(db, projectId, itemId, ct));
            var contentTypeErrors = AttachmentUploadValidator.ValidateContentType(http.Request);
            if (contentTypeErrors.Count > 0) return Results.ValidationProblem(contentTypeErrors);
            var form = await http.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            var errors = AttachmentUploadValidator.Validate(file);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var root = StorageRoot(configuration);
            Directory.CreateDirectory(root);
            var key = Guid.NewGuid().ToString("N");
            var path = Path.Combine(root, key);
            var saved = false;
            try
            {
                await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await file!.CopyToAsync(stream, ct);
                if (new FileInfo(path).Length != file!.Length || !await AttachmentUploadValidator.ValidSignatureAsync(path, file.ContentType, ct))
                    return Results.ValidationProblem(AttachmentUploadValidator.InvalidContent());
                var attachment = new WorkAttachment { WorkItemId = itemId, UploadedByUserId = ApiRules.UserId(http),
                    StorageKey = key, OriginalName = Path.GetFileName(file.FileName), ContentType = file.ContentType,
                    SizeBytes = file.Length };
                db.WorkAttachments.Add(attachment);
                WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "attachment.created", "Attachment",
                    attachment.Id, after: new { attachment.WorkItemId, attachment.OriginalName, attachment.ContentType, attachment.SizeBytes },
                    workItemId: itemId);
                files.DeleteOnFailure(path);
                await db.SaveChangesAsync(ct);
                saved = true;
                return Results.Created($"/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/attachments/{attachment.Id}",
                    ToDto(attachment));
            }
            finally
            {
                if (!saved)
                    File.Delete(path);
            }
        }).DisableAntiforgery();
        group.MapGet("/{attachmentId:guid}", async (Guid workspaceId, Guid projectId, Guid itemId, Guid attachmentId,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db,
            IConfiguration configuration, CancellationToken ct) =>
        {
            await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            await WorkItemSupport.ItemAsync(db, projectId, itemId, ct);
            var attachment = await AttachmentAsync(db, itemId, attachmentId, ct);
            var path = Path.Combine(StorageRoot(configuration), attachment.StorageKey);
            ApiRules.Require(File.Exists(path), 404, "Resource not found.");
            return Results.File(path, attachment.ContentType, attachment.OriginalName);
        });
        group.MapDelete("/{attachmentId:guid}", async (Guid workspaceId, Guid projectId, Guid itemId, Guid attachmentId,
            HttpContext http, WorkspaceAccessService access, ProjectAccessService projects, FlowForgeDbContext db,
            IConfiguration configuration, AttachmentFileActions files, CancellationToken ct) =>
        {
            var (project, role) = await WorkItemSupport.ProjectAsync(workspaceId, projectId, http, access, projects, ct);
            WorkItemSupport.Write(project, role);
            WorkItemSupport.ItemWritable(await WorkItemSupport.ItemAsync(db, projectId, itemId, ct));
            var attachment = await AttachmentAsync(db, itemId, attachmentId, ct);
            ApiRules.Require(attachment.UploadedByUserId == ApiRules.UserId(http) || ProjectPermissions.CanManage(role, true),
                403, "Only the uploader or a project manager can delete this attachment.");
            attachment.DeletedAt = DateTimeOffset.UtcNow;
            WorkItemSupport.Activity(db, workspaceId, projectId, ApiRules.UserId(http), "attachment.deleted", "Attachment",
                attachmentId, before: new { attachment.OriginalName, attachment.ContentType, attachment.SizeBytes },
                workItemId: itemId);
            await db.SaveChangesAsync(ct);
            files.DeleteAfterCommit(Path.Combine(StorageRoot(configuration), attachment.StorageKey));
            return Results.NoContent();
        });
        var activity = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}")
            .WithTags("Activity").RequireAuthorization();
        activity.MapGet("/activity", async (Guid workspaceId, HttpContext http, WorkspaceAccessService access,
            ProjectAccessService projects, FlowForgeDbContext db, Guid? projectId, Guid? workItemId,
            int? page, int? pageSize, CancellationToken ct) =>
        {
            var role = await ApiRules.RoleAsync(workspaceId, http, access, ct);
            var errors = ActivityListQueryValidator.Validate(projectId, workItemId, page, pageSize);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (projectId != null)
                ApiRules.Require(await projects.VisibleProjects(workspaceId, ApiRules.UserId(http), role)
                    .AnyAsync(x => x.Id == projectId, ct), 404, "Resource not found.");
            if (workItemId != null)
                ApiRules.Require(await db.WorkItems.AnyAsync(x => x.Id == workItemId && x.ProjectId == projectId, ct),
                    404, "Resource not found.");
            var visible = projects.VisibleProjects(workspaceId, ApiRules.UserId(http), role).Select(x => x.Id);
            var query = db.ActivityRecords.AsNoTracking().Where(x => x.WorkspaceId == workspaceId &&
                (x.ProjectId == null || visible.Contains(x.ProjectId.Value)));
            if (projectId != null) query = query.Where(x => x.ProjectId == projectId);
            if (workItemId != null) query = query.Where(x => x.WorkItemId == workItemId);
            var (number, size) = PaginationValidator.Defaults(page, pageSize);
            var total = await query.CountAsync(ct);
            var records = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
                .Skip((number - 1) * size).Take(size).ToListAsync(ct);
            var items = records.Select(WorkItemSupport.ActivityDto).ToList();
            return Results.Ok(new PageDto<ActivityDto>(items, total, number, size));
        });
        return app;
    }
    private static string StorageRoot(IConfiguration configuration) => Path.GetFullPath(
        configuration["Attachments:StoragePath"] ?? Path.Combine(AppContext.BaseDirectory, "private-attachments"));
    private static async Task<WorkAttachment> AttachmentAsync(FlowForgeDbContext db, Guid itemId, Guid attachmentId, CancellationToken ct)
    {
        var attachment = await db.WorkAttachments.SingleOrDefaultAsync(x => x.WorkItemId == itemId && x.Id == attachmentId && x.DeletedAt == null, ct);
        ApiRules.Require(attachment != null, 404, "Resource not found.");
        return attachment!;
    }
    private static AttachmentDto ToDto(WorkAttachment x) => new(x.Id, x.OriginalName, x.ContentType, x.SizeBytes,
        x.UploadedByUserId, x.CreatedAt);
}
