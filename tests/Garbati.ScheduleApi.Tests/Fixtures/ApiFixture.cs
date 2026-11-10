using Garbati.ScheduleApi.Authorization;
using Garbati.ScheduleApi.Clients.Interfaces;
using Garbati.ScheduleApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace Garbati.ScheduleApi.Tests.Fixtures;

// Runs the real app in memory (WebApplicationFactory) against its own Postgres container, so requests go
// through the full pipeline: correlation id, authentication, authorization, controllers and the database.
// IOrchestratorApiClient/ILedgerApiClient are replaced with hand-rolled fakes here (same judgment call
// Garbati.OrchestratorApi's own ApiFixture makes for IIdentityApiClient/ILedgerApiClient: this service is
// tested against mocked/stubbed HTTP clients, not real locally-run OrchestratorApi/LedgerApi instances -
// see the PR description for what that means this suite cannot verify). The "Testing" environment also
// disables StandingInstructionSchedulerService (Program.cs) so no real background tick ever races a test's
// own data - Scheduling/StandingInstructionRunnerTests exercises that logic directly instead.
public class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public FakeOrchestratorApiClient OrchestratorApiClient { get; } = new();
    public FakeLedgerApiClient LedgerApiClient { get; } = new();

    // For every scope there is a client holding only that scope, and one holding every scope except it.
    public static string OnlyClient(string scope) => $"only-{scope}";
    public static string AllButClient(string scope) => $"all-but-{scope}";
    public static string KeyFor(string clientId) => $"test-key-{clientId}";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using (var ctx = CreateContext())
        {
            await ctx.Database.MigrateAsync();
        }

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:ScheduleDb"] = _container.GetConnectionString(),
            ["Serilog:MinimumLevel:Default"] = "Warning",
            ["Swagger:Enabled"] = "true",
            ["DownstreamServices:OrchestratorApi:BaseUrl"] = "http://unused.invalid",
            ["DownstreamServices:OrchestratorApi:ApiKey"] = "unused-placeholder",
            ["DownstreamServices:LedgerApi:BaseUrl"] = "http://unused.invalid",
            ["DownstreamServices:LedgerApi:ApiKey"] = "unused-placeholder",
        };

        var clients = ScheduleApiScopes.All
            .SelectMany(scope => new[]
            {
                (Id: OnlyClient(scope), Scopes: new[] { scope }),
                (Id: AllButClient(scope), Scopes: ScheduleApiScopes.All.Where(s => s != scope).ToArray()),
            })
            .ToList();

        for (var i = 0; i < clients.Count; i++)
        {
            settings[$"ApiKeys:Clients:{i}:ClientId"] = clients[i].Id;
            settings[$"ApiKeys:Clients:{i}:KeyHash"] = Convert.ToBase64String(ApiKeyHasher.Hash(KeyFor(clients[i].Id)));
            for (var j = 0; j < clients[i].Scopes.Length; j++)
            {
                settings[$"ApiKeys:Clients:{i}:Scopes:{j}"] = clients[i].Scopes[j];
            }
        }

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOrchestratorApiClient>();
                services.AddSingleton<IOrchestratorApiClient>(OrchestratorApiClient);
                services.RemoveAll<ILedgerApiClient>();
                services.AddSingleton<ILedgerApiClient>(LedgerApiClient);
            });
        });
    }

    public ScheduleDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ScheduleDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ScheduleDbContext(options);
    }

    // An HTTP client for the in-memory app, sending the given client's key (or no key when null).
    public HttpClient CreateClient(string? clientId)
    {
        var client = Factory.CreateClient();
        if (clientId is not null)
        {
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, KeyFor(clientId));
        }
        return client;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<ApiFixture>;
