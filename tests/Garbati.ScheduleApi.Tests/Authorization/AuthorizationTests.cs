using System.Net;
using System.Text;
using System.Text.Json;
using Garbati.ScheduleApi.Authorization;
using Garbati.ScheduleApi.Middleware;
using Garbati.ScheduleApi.Tests.Fixtures;

namespace Garbati.ScheduleApi.Tests.Authorization;

// Task file / docs/DESIGN.md Section 4: every endpoint named with its required scope. Same test shape as
// every sibling service's AuthorizationTests.
[Collection("Api")]
public class AuthorizationTests(ApiFixture fixture)
{
    private static string CreateBody() => """
        {"idempotencyKey":"auth-test-create","name":"auth-test-create","customerId":"CUS-AUTHTEST","fromAccountType":"Savings","fromAccountId":"1000000001",
         "toAccountType":"Savings","toAccountId":"1000000002","transferType":"AccountTransfer","instructionType":"Fixed",
         "priority":"Medium","amount":100,"currency":"NGN","validFrom":"2026-01-01","recurrenceType":"Periodic",
         "recurrenceFrequency":"Months","recurrenceInterval":1,"recurrenceOnDay":1,"allowPartialTransfer":false}
        """;
    private static string UpdateBody() => """
        {"amount":100,"validFrom":"2026-01-01","recurrenceType":"Periodic","recurrenceFrequency":"Months",
         "recurrenceInterval":1,"recurrenceOnDay":1,"priority":"Medium","allowPartialTransfer":false}
        """;
    private static string StatusBody() => """{"status":"Disabled"}""";

    public static TheoryData<string, string, string?, string> Endpoints => new()
    {
        { "POST", "/v1/standing-instructions", CreateBody(), ScheduleApiScopes.Write },
        { "GET", "/v1/standing-instructions/SI-DOESNOTEXIST", null, ScheduleApiScopes.Read },
        { "GET", "/v1/standing-instructions?customerId=CUS-DOESNOTEXIST", null, ScheduleApiScopes.Read },
        { "PUT", "/v1/standing-instructions/SI-DOESNOTEXIST", UpdateBody(), ScheduleApiScopes.Write },
        { "DELETE", "/v1/standing-instructions/SI-DOESNOTEXIST", null, ScheduleApiScopes.Write },
        { "PATCH", "/v1/standing-instructions/SI-DOESNOTEXIST/status", StatusBody(), ScheduleApiScopes.Write },
        { "GET", "/v1/standing-instructions/SI-DOESNOTEXIST/runs", null, ScheduleApiScopes.Read },
    };

    private static HttpRequestMessage Request(string method, string path, string? body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        return request;
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Returns401_WithProblemDetails_WhenNoApiKeyIsSent(string method, string path, string? body, string scope)
    {
        _ = scope;
        using var client = fixture.CreateClient(clientId: null);

        using var response = await client.SendAsync(Request(method, path, body));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ApiKeyAuthenticationHandler.SchemeName, response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var correlationId = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Assert.Equal(correlationId, problem.RootElement.GetProperty("correlationId").GetString());
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Returns403_WhenTheKeyLacksTheRequiredScope(string method, string path, string? body, string scope)
    {
        using var client = fixture.CreateClient(ApiFixture.AllButClient(scope));

        using var response = await client.SendAsync(Request(method, path, body));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task ReachesTheEndpoint_WhenTheKeyHoldsOnlyTheRequiredScope(string method, string path, string? body, string scope)
    {
        using var client = fixture.CreateClient(ApiFixture.OnlyClient(scope));

        using var response = await client.SendAsync(Request(method, path, body));

        // Past authorization the controller answers normally (200, 404, 409 or 422 depending on the
        // data) - never 401/403.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns401_ForAnUnknownApiKey()
    {
        using var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, "idk_not_a_real_key");

        using var response = await client.GetAsync("/v1/standing-instructions/SI-DOESNOTEXIST");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
