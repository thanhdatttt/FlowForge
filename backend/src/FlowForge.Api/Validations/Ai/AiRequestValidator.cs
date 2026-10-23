using FlowForge.Api.Contracts.Ai;
using FlowForge.Application.Ai;

namespace FlowForge.Api.Validations.Ai;

public static class AiRequestValidator
{
    public static Dictionary<string, string[]> Validate(AiTaskProposalRequestDto request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.FeatureDescription) || request.FeatureDescription.Length > 10000)
            errors["featureDescription"] = ["Feature description must be 1–10000 characters."];
        return errors;
    }

    public static Dictionary<string, string[]> Validate(AiTaskAcceptanceRequestDto request)
    {
        var errors = AiOutputRules.ValidateTasks(request.Tasks);
        if (!request.Accepted) errors["accepted"] = ["Explicit acceptance is required before creating tasks."];
        if (string.IsNullOrWhiteSpace(request.PreviewToken) || request.PreviewToken.Length > 32768)
            errors["previewToken"] = ["A valid preview token is required."];
        return errors;
    }
}
