using System.Text.Json.Serialization;

namespace Garbati.ScheduleApi.Clients.Implementations;

// A minimal mirror of Microsoft.AspNetCore.Mvc.ProblemDetails for reading a response, not writing one.
// [JsonExtensionData] captures every extra top-level member ASP.NET's own ProblemDetails JSON converter
// flattens Extensions into (decisionId, reasonCode, correlationId), without this service needing to know
// the full set in advance. Mirrors Garbati.OrchestratorApi.Clients.Implementations.ProblemDetailsBody.
internal sealed class ProblemDetailsBody
{
    public string? Title { get; set; }
    public string? Detail { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object?>? Extensions { get; set; }

    public string? ExtensionString(string key) =>
        Extensions is not null && Extensions.TryGetValue(key, out var value) && value is not null
            ? value.ToString()
            : null;
}
