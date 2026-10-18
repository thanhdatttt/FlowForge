namespace FlowForge.Api.SmokeTests;

public sealed record SmokeReport(string RunAt, string BaseUrl, IReadOnlyList<SmokeCheck> Checks,
    IReadOnlyList<SmokeCheck> Requests, SmokeTotals Totals);
