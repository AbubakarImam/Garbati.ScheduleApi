namespace Garbati.ScheduleApi.Authorization;

// Each scope is also the name of the authorization policy that requires it (same pattern as
// LedgerApi.Authorization.LedgerScopes / Garbati.OrchestratorApi.Authorization.OrchestratorScopes).
// Two scopes, matching docs/DESIGN.md Section 4's table exactly: scheduleapi.read covers every GET,
// scheduleapi.write covers every endpoint that creates or mutates a StandingInstruction.
public static class ScheduleApiScopes
{
    public const string ClaimType = "scope";

    public const string Read = "scheduleapi.read";
    public const string Write = "scheduleapi.write";

    public static readonly IReadOnlyList<string> All = [Read, Write];
}
