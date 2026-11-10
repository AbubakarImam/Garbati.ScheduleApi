using Garbati.ScheduleApi.Authorization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Garbati.ScheduleApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Garbati.ScheduleApi.Tests.Controllers;

// POST /v1/standing-instructions (docs/DESIGN.md Section 4). Real HTTP-level tests through the full
// pipeline (ApiFixture), including - per the task file's single most important requirement - one test
// per enum-shaped request field posting the exact string a real caller would send, proving it round-trips
// through real JSON serialization on the test server (the LedgerApi PR #31 lesson: a raw C# enum with no
// JsonStringEnumConverter only accepts the ordinal integer, never caught by an in-process test).
[Collection("Api")]
public class CreateStandingInstructionTests(ApiFixture fixture)
{
    private HttpClient Client() => fixture.CreateClient(ApiFixture.OnlyClient(ScheduleApiScopes.Write));

    [Fact]
    public async Task Create_WithAValidRequest_Returns200_WithTheCreatedInstruction()
    {
        using var client = Client();

        var created = await TestRequests.CreateAsync(client);

        Assert.False(string.IsNullOrWhiteSpace(created.GetProperty("standingInstructionId").GetString()));
        Assert.Equal("Active", created.GetProperty("status").GetString());
        Assert.Equal(500, created.GetProperty("amount").GetDecimal());
    }

    // --- idempotency (docs/DESIGN.md Appendix A - a gap in the original design table, found and fixed
    // during the build; mirrors LedgerApi's POST /api/accounts / Garbati.IdentityApi's POST /v1/customers) ---

    [Fact]
    public async Task Create_RetriedWithTheSameKeyAndBody_ReplaysTheOriginalInstruction_CreatesNoSecondRow()
    {
        using var client = Client();
        var key = $"idem-replay-{Guid.NewGuid():N}";
        var request = TestRequests.CreateRequest(idempotencyKey: key);

        var first = await TestRequests.CreateAsync(client, request);
        var second = await TestRequests.CreateAsync(client, request);

        Assert.Equal(first.GetProperty("standingInstructionId").GetString(), second.GetProperty("standingInstructionId").GetString());

        await using var dbContext = fixture.CreateContext();
        var matchingRows = await dbContext.StandingInstructions.CountAsync(s => s.IdempotencyKey == key);
        Assert.Equal(1, matchingRows); // exactly one row - the retry did not create a second instruction.
    }

    [Fact]
    public async Task Create_RetriedWithTheSameKeyButADifferentBody_Returns409()
    {
        using var client = Client();
        var key = $"idem-conflict-{Guid.NewGuid():N}";

        await TestRequests.CreateAsync(client, TestRequests.CreateRequest(idempotencyKey: key, amount: 500m));

        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(idempotencyKey: key, amount: 999m), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // QA's own repro: two requests with the IDENTICAL key AND identical Name, fired truly concurrently
    // (Task.WhenAll, not sequential awaits), race between the upfront idempotency check and the actual
    // insert - both can pass every pre-check before either commits, since the OTHER request hasn't
    // inserted yet at the moment each one checks. The loser's insert then violates BOTH unique
    // constraints (Name AND the idempotency index) at once; Postgres reports only one constraint name
    // back, which could be ix_standing_instructions_name - and naively treating that as "a genuine name
    // conflict" (without first checking for the caller's own idempotent match) would tell this caller
    // "duplicate name" instead of returning its own successful response, which is exactly the kind of
    // error a real caller would reasonably react to by retrying with a NEW key - recreating the duplicate-
    // recurring-payment risk this whole feature exists to close.
    [Fact]
    public async Task Create_WithTwoTrulyConcurrentRequests_SameKeyAndSameName_NeitherSeesADuplicateNameConflict()
    {
        using var client = Client();
        var key = $"idem-race-{Guid.NewGuid():N}";
        var body = JsonSerializer.Serialize(TestRequests.CreateRequest(idempotencyKey: key), TestRequests.JsonOptions);

        Task<HttpResponseMessage> SendAsync() => client.PostAsync(
            "/v1/standing-instructions", new StringContent(body, Encoding.UTF8, "application/json"));

        var responses = await Task.WhenAll(SendAsync(), SendAsync());

        try
        {
            foreach (var response in responses)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            var ids = new List<string>();
            foreach (var response in responses)
            {
                var parsed = await response.Content.ReadFromJsonAsync<JsonElement>();
                ids.Add(parsed.GetProperty("standingInstructionId").GetString()!);
            }
            Assert.Equal(ids[0], ids[1]); // both requests resolved to the exact same instruction.
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var dbContext = fixture.CreateContext();
        var matchingRows = await dbContext.StandingInstructions.CountAsync(s => s.IdempotencyKey == key);
        Assert.Equal(1, matchingRows); // exactly one row - the race did not create a second instruction.
    }

    [Fact]
    public async Task Create_WithoutAnIdempotencyKey_Returns400()
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(idempotencyKey: ""), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- enum-shaped request fields: each posted as the exact string a real caller sends -------------

    [Theory]
    [InlineData("Savings")]
    public async Task Create_AcceptsFromAccountType_AsARealJsonString(string value)
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(fromAccountType: value), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(value, body.GetProperty("fromAccountType").GetString());
    }

    [Theory]
    [InlineData("Savings")]
    public async Task Create_AcceptsToAccountType_AsARealJsonString(string value)
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(toAccountType: value), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(value, body.GetProperty("toAccountType").GetString());
    }

    [Theory]
    [InlineData("AccountTransfer")]
    public async Task Create_AcceptsTransferType_AsARealJsonString(string value)
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(transferType: value), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(value, body.GetProperty("transferType").GetString());
    }

    [Theory]
    [InlineData("Fixed")]
    public async Task Create_AcceptsInstructionType_AsARealJsonString(string value)
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(instructionType: value), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(value, body.GetProperty("instructionType").GetString());
    }

    [Theory]
    [InlineData("Urgent")]
    [InlineData("High")]
    [InlineData("Medium")]
    [InlineData("Low")]
    public async Task Create_AcceptsPriority_AsARealJsonString(string value)
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(priority: value), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(value, body.GetProperty("priority").GetString());
    }

    [Theory]
    [InlineData("Periodic")]
    public async Task Create_AcceptsRecurrenceType_AsARealJsonString(string value)
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(recurrenceType: value), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(value, body.GetProperty("recurrenceType").GetString());
    }

    [Theory]
    [InlineData("Days")]
    [InlineData("Weeks")]
    [InlineData("Months")]
    [InlineData("Years")]
    public async Task Create_AcceptsRecurrenceFrequency_AsARealJsonString(string value)
    {
        using var client = Client();
        var request = TestRequests.CreateRequest(
            recurrenceFrequency: value,
            recurrenceOnDay: value == "Weeks" ? 1 : 1); // 1 is valid for every frequency this test uses.
        using var response = await client.PostAsJsonAsync("/v1/standing-instructions", request, TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(value, body.GetProperty("recurrenceFrequency").GetString());
    }

    // --- shape validation (400) ------------------------------------------------------------------------

    [Fact]
    public async Task Create_WithAnUnrecognizedEnumString_Returns400()
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(priority: "SuperUrgent"), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithADuplicateName_Returns409()
    {
        using var client = Client();
        var name = $"dup-{Guid.NewGuid():N}";
        await TestRequests.CreateAsync(client, TestRequests.CreateRequest(name: name));

        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(name: name), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // --- the not-yet-supported financing combinations: 422, not 400 (docs/DESIGN.md Section 4) --------

    [Theory]
    [InlineData("Loan")]
    public async Task Create_WithFromAccountTypeLoan_Returns422(string accountType)
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(fromAccountType: accountType), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("Loan")]
    public async Task Create_WithToAccountTypeLoan_Returns422(string accountType)
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(toAccountType: accountType), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("LoanRepayment")]
    [InlineData("ChargePayment")]
    [InlineData("InterestTransfer")]
    [InlineData("LoanDownPayment")]
    public async Task Create_WithANonAccountTransferTransferType_Returns422(string transferType)
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(transferType: transferType), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithInstructionTypeDues_Returns422()
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(instructionType: "Dues"), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithRecurrenceTypeAsPerDues_Returns422()
    {
        using var client = Client();
        using var response = await client.PostAsJsonAsync(
            "/v1/standing-instructions", TestRequests.CreateRequest(recurrenceType: "AsPerDues"), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
