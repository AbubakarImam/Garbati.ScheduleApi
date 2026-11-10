using Garbati.ScheduleApi.Configuration;

namespace Garbati.ScheduleApi.Tests.Configuration;

// Runs at startup (ValidateOnStart, Program.cs) so a missing base URL or key surfaces immediately rather
// than deep inside the scheduler's hourly tick.
public class DownstreamServicesOptionsValidatorTests
{
    private static DownstreamServicesOptions Valid() => new()
    {
        OrchestratorApi = new DownstreamServiceOptions { BaseUrl = "http://orchestrator.invalid", ApiKey = "key" },
        LedgerApi = new DownstreamServiceOptions { BaseUrl = "http://ledger.invalid", ApiKey = "key" },
    };

    [Fact]
    public void Validate_WithBothServicesConfigured_Succeeds()
    {
        var result = new DownstreamServicesOptionsValidator().Validate(null, Valid());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    public void Validate_WhenOrchestratorApiBaseUrlIsMissingOrInvalid_Fails(string baseUrl)
    {
        var options = Valid();
        options.OrchestratorApi.BaseUrl = baseUrl;

        var result = new DownstreamServicesOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("DownstreamServices:OrchestratorApi:BaseUrl"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenOrchestratorApiApiKeyIsMissing_Fails(string apiKey)
    {
        var options = Valid();
        options.OrchestratorApi.ApiKey = apiKey;

        var result = new DownstreamServicesOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("DownstreamServices:OrchestratorApi:ApiKey"));
    }

    [Fact]
    public void Validate_WhenLedgerApiIsMissing_Fails()
    {
        var options = Valid();
        options.LedgerApi.BaseUrl = "";
        options.LedgerApi.ApiKey = "";

        var result = new DownstreamServicesOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("DownstreamServices:LedgerApi:BaseUrl"));
        Assert.Contains(result.Failures!, f => f.Contains("DownstreamServices:LedgerApi:ApiKey"));
    }
}
