namespace FlowForge.Api.SmokeTests;

public sealed record SmokeTotals(int Checks, int Passed, int Failed, int RecordedHttpChecks,
    int StateChecks, int AllHttpRequests, int PollingHttpRequests);
