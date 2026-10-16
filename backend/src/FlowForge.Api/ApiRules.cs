using System.IdentityModel.Tokens.Jwt;
using FlowForge.Domain.Workspaces;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Workspaces;

namespace FlowForge.Api;

public sealed class ApiFailure(int status, string title) : Exception(title)
{
    public int Status { get; } = status;
}

// Request identity, resource access, and business-state guards. Input validation lives in Validation/.
public static class ApiRules
{
    public static Guid UserId(HttpContext context) => Guid.Parse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
    public static void Require(bool condition, int status, string title)
    {
        if (!condition) throw new ApiFailure(status, title);
    }
    public static async Task<WorkspaceRole> RoleAsync(Guid workspaceId, HttpContext context,
        WorkspaceAccessService access, CancellationToken ct)
    {
        var role = await access.GetRoleAsync(workspaceId, UserId(context), ct);
        Require(role != null, 404, "Resource not found.");
        return role!.Value;
    }
}

public sealed class WorkspaceMutationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) ||
            !Guid.TryParse(http.Request.RouteValues["workspaceId"]?.ToString(), out var workspaceId))
            return await next(context);
        var db = http.RequestServices.GetRequiredService<FlowForgeDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(http.RequestAborted);
        if (await WorkspaceLock.AcquireAsync(db, workspaceId, http.RequestAborted) is null)
            return Results.NotFound();
        var result = await next(context);
        await transaction.CommitAsync(http.RequestAborted);
        return result;
    }
}
