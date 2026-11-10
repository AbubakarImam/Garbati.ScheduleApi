using Microsoft.Extensions.Options;

namespace Garbati.ScheduleApi.Configuration;

// This service's own outbound credentials: the X-Api-Key value it presents TO OrchestratorApi/LedgerApi,
// and the base URL it calls them at. Deliberately separate from Authorization/ApiKeyOptions, which holds
// only hashes of the keys OTHER clients present TO this service - an outbound key must be a usable
// secret, not a one-way hash, so it cannot live in the same options type or configuration section.
public class DownstreamServiceOptions
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

public class DownstreamServicesOptions
{
    // The scheduler's own transfer submission (docs/DESIGN.md 3.1/3.3).
    public DownstreamServiceOptions OrchestratorApi { get; set; } = new();

    // The narrow, read-only AllowPartialTransfer balance exception (docs/DESIGN.md 3.2, D-33 decision 4) -
    // the only call ScheduleApi ever makes directly to LedgerApi.
    public DownstreamServiceOptions LedgerApi { get; set; } = new();
}

// Runs at startup (ValidateOnStart): a missing base URL or key would otherwise surface only the first
// time the scheduler's hourly tick tries to use it, as a confusing failure deep inside a scheduled run.
public class DownstreamServicesOptionsValidator : IValidateOptions<DownstreamServicesOptions>
{
    public ValidateOptionsResult Validate(string? name, DownstreamServicesOptions options)
    {
        var errors = new List<string>();

        Check(options.OrchestratorApi, "DownstreamServices:OrchestratorApi", errors);
        Check(options.LedgerApi, "DownstreamServices:LedgerApi", errors);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void Check(DownstreamServiceOptions service, string prefix, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(service.BaseUrl) || !Uri.TryCreate(service.BaseUrl, UriKind.Absolute, out _))
        {
            errors.Add($"{prefix}:BaseUrl must be an absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(service.ApiKey))
        {
            errors.Add($"{prefix}:ApiKey is required.");
        }
    }
}
