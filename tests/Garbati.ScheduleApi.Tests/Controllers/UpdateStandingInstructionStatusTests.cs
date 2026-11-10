using Garbati.ScheduleApi.Authorization;
using System.Net;
using System.Net.Http.Json;
using Garbati.ScheduleApi.Tests.Fixtures;

namespace Garbati.ScheduleApi.Tests.Controllers;

// PATCH /v1/standing-instructions/{id}/status (docs/DESIGN.md Section 4) - Active <-> Disabled only.
[Collection("Api")]
public class UpdateStandingInstructionStatusTests(ApiFixture fixture)
{
    private HttpClient WriteClient() => fixture.CreateClient(ApiFixture.OnlyClient(ScheduleApiScopes.Write));

    // --- the two accepted status values, each posted as the real JSON string a caller would send -------

    [Theory]
    [InlineData("Disabled")]
    [InlineData("Active")]
    public async Task UpdateStatus_AcceptsActiveOrDisabled_AsARealJsonString(string status)
    {
        using var client = WriteClient();
        var created = await TestRequests.CreateAsync(client);
        var id = created.GetProperty("standingInstructionId").GetString();

        using var response = await client.PatchAsJsonAsync($"/v1/standing-instructions/{id}/status", new { status }, TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(status, body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UpdateStatus_WithStatusDeleted_Returns400_NotAllowedThroughThisEndpoint()
    {
        using var client = WriteClient();
        var created = await TestRequests.CreateAsync(client);
        var id = created.GetProperty("standingInstructionId").GetString();

        using var response = await client.PatchAsJsonAsync(
            $"/v1/standing-instructions/{id}/status", new { status = "Deleted" }, TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_WithAnUnrecognizedValue_Returns400()
    {
        using var client = WriteClient();
        var created = await TestRequests.CreateAsync(client);
        var id = created.GetProperty("standingInstructionId").GetString();

        using var response = await client.PatchAsJsonAsync(
            $"/v1/standing-instructions/{id}/status", new { status = "Paused" }, TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_ForAnUnknownId_Returns404()
    {
        using var client = WriteClient();

        using var response = await client.PatchAsJsonAsync(
            "/v1/standing-instructions/SI-DOESNOTEXIST/status", new { status = "Disabled" }, TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_OnADeletedInstruction_Returns422_CannotBeReactivatedThroughThisEndpoint()
    {
        using var client = WriteClient();
        var created = await TestRequests.CreateAsync(client);
        var id = created.GetProperty("standingInstructionId").GetString();
        await client.DeleteAsync($"/v1/standing-instructions/{id}");

        using var response = await client.PatchAsJsonAsync(
            $"/v1/standing-instructions/{id}/status", new { status = "Active" }, TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
