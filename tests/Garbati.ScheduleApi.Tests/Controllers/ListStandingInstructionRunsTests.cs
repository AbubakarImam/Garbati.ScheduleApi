using Garbati.ScheduleApi.Authorization;
using System.Net;
using System.Net.Http.Json;
using Garbati.ScheduleApi.Entities;
using Garbati.ScheduleApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Garbati.ScheduleApi.Tests.Controllers;

// GET /v1/standing-instructions/{id}/runs (docs/DESIGN.md Section 4) - paged run history. The scheduler
// itself is disabled in the "Testing" environment (ApiFixture's own comment), so run rows are inserted
// directly here - this test is about the endpoint's read/paging behavior, not the scheduler.
[Collection("Api")]
public class ListStandingInstructionRunsTests(ApiFixture fixture)
{
    private HttpClient WriteClient() => fixture.CreateClient(ApiFixture.OnlyClient(ScheduleApiScopes.Write));
    private HttpClient ReadClient() => fixture.CreateClient(ApiFixture.OnlyClient(ScheduleApiScopes.Read));

    [Fact]
    public async Task ListRuns_ReturnsThePersistedRunHistory_MostRecentFirst()
    {
        using var write = WriteClient();
        var created = await TestRequests.CreateAsync(write);
        var publicId = created.GetProperty("standingInstructionId").GetString()!;

        await using var dbContext = fixture.CreateContext();
        var internalId = await dbContext.StandingInstructions.AsNoTracking()
            .Where(s => s.StandingInstructionId == publicId).Select(s => s.Id).SingleAsync();

        dbContext.StandingInstructionRuns.AddRange(
            new StandingInstructionRun
            {
                StandingInstructionId = internalId, ScheduledDate = new DateOnly(2026, 1, 1),
                Status = StandingInstructionRunStatus.Success, Amount = 500m, ExecutedAt = DateTimeOffset.UtcNow,
                IdempotencyKey = $"sched:{internalId}:2026-01-01", OrchestratorReference = "TXN-OLD",
            },
            new StandingInstructionRun
            {
                StandingInstructionId = internalId, ScheduledDate = new DateOnly(2026, 2, 1),
                Status = StandingInstructionRunStatus.Failed, Amount = 500m, ExecutedAt = DateTimeOffset.UtcNow,
                IdempotencyKey = $"sched:{internalId}:2026-02-01", ErrorMessage = "insufficient funds",
            });
        await dbContext.SaveChangesAsync();

        using var read = ReadClient();
        using var response = await read.GetAsync($"/v1/standing-instructions/{publicId}/runs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var items = body.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        // Most-recent-first: the February run (Failed) before the January run (Success).
        Assert.Equal("Failed", items[0].GetProperty("status").GetString());
        Assert.Equal("Success", items[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task ListRuns_ForAnUnknownStandingInstructionId_Returns404()
    {
        using var client = ReadClient();

        using var response = await client.GetAsync("/v1/standing-instructions/SI-DOESNOTEXIST/runs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListRuns_WithNoRunsYet_ReturnsAnEmptyPage()
    {
        using var write = WriteClient();
        var created = await TestRequests.CreateAsync(write);
        var publicId = created.GetProperty("standingInstructionId").GetString();

        using var read = ReadClient();
        using var response = await read.GetAsync($"/v1/standing-instructions/{publicId}/runs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(0, body.GetProperty("items").GetArrayLength());
    }
}
