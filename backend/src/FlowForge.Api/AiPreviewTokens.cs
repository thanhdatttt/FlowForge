using System.Security.Cryptography;
using System.Text.Json;
using FlowForge.Application.Ai;
using FlowForge.Infrastructure.Ai;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace FlowForge.Api;

public sealed class AiPreviewTokens(IDataProtectionProvider protection, TimeProvider clock, IOptions<AiOptions> options)
{
    private readonly IDataProtector protector = protection.CreateProtector("FlowForge.AI.TaskPreview.v1");

    public (string Token, DateTimeOffset ExpiresAt) Create(Guid workspaceId, Guid projectId, Guid userId, IReadOnlyList<AiTaskProposal> tasks)
    {
        var expires = clock.GetUtcNow().AddMinutes(options.Value.PreviewLifetimeMinutes);
        var payload = new Preview(Guid.NewGuid(), workspaceId, projectId, userId, expires, tasks.Select(x => x.Id!).ToArray());
        return (protector.Protect(JsonSerializer.Serialize(payload)), expires);
    }

    public Preview Read(string token, Guid workspaceId, Guid projectId, Guid userId)
    {
        Preview? payload;
        try { payload = JsonSerializer.Deserialize<Preview>(protector.Unprotect(token)); }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        { throw new ApiFailure(400, "Invalid preview token."); }
        ApiRules.Require(payload != null, 400, "Invalid preview token.");
        ApiRules.Require(payload!.WorkspaceId == workspaceId && payload.ProjectId == projectId && payload.UserId == userId,
            404, "Resource not found.");
        ApiRules.Require(payload.ExpiresAt > clock.GetUtcNow(), 410, "The preview expired. Generate a new proposal.");
        return payload;
    }

    public sealed record Preview(Guid BatchId, Guid WorkspaceId, Guid ProjectId, Guid UserId, DateTimeOffset ExpiresAt, IReadOnlyList<string> ProposalIds);
}
