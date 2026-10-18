namespace FlowForge.Api.SmokeTests;

public sealed record SmokeCheck(string Kind, string Check, object Expected, object Actual, bool Passed,
    string? Method = null, string? Path = null, bool? Polling = null);
