using Garbati.ScheduleApi.Authorization;
using System.Net;
using System.Net.Http.Json;
using Garbati.ScheduleApi.Tests.Fixtures;

namespace Garbati.ScheduleApi.Tests.Controllers;

// PUT /v1/standing-instructions/{id} (docs/DESIGN.md Section 4) - real in-place edit: amount, schedule,
// priority, AllowPartialTransfer.
[Collection("Api")]
public class UpdateStandingInstructionTests(ApiFixture fixture)
{
    private HttpClient WriteClient() => fixture.CreateClient(ApiFixture.OnlyClient(ScheduleApiScopes.Write));

    private static object UpdateRequest(
        decimal amount = 999m, string priority = "High", bool allowPartialTransfer = true,
        string recurrenceType = "Periodic", string recurrenceFrequency = "Months") => new
    {
        amount,
        validFrom = "2026-02-01",
        validTill = (string?)null,
        recurrenceType,
        recurrenceFrequency,
        recurrenceInterval = 2,
        recurrenceOnDay = 15,
        priority,
        allowPartialTransfer,
    };

    [Fact]
    public async Task Update_WithAValidRequest_Returns200_WithTheUpdatedFields()
    {
        using var client = WriteClient();
        var created = await TestRequests.CreateAsync(client);
        var id = created.GetProperty("standingInstructionId").GetString();

        using var response = await client.PutAsJsonAsync($"/v1/standing-instructions/{id}", UpdateRequest(), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(999, body.GetProperty("amount").GetDecimal());
        Assert.Equal("High", body.GetProperty("priority").GetString());
        Assert.True(body.GetProperty("allowPartialTransfer").GetBoolean());
    }

    [Fact]
    public async Task Update_ForAnUnknownId_Returns404()
    {
        using var client = WriteClient();

        using var response = await client.PutAsJsonAsync("/v1/standing-instructions/SI-DOESNOTEXIST", UpdateRequest(), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_OnADeletedInstruction_Returns422()
    {
        using var client = WriteClient();
        var created = await TestRequests.CreateAsync(client);
        var id = created.GetProperty("standingInstructionId").GetString();
        await client.DeleteAsync($"/v1/standing-instructions/{id}");

        using var response = await client.PutAsJsonAsync($"/v1/standing-instructions/{id}", UpdateRequest(), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // RecurrenceType is still restricted to Periodic on PUT - a caller cannot use the edit endpoint to
    // smuggle an instruction into the not-yet-supported AsPerDues after creation.
    [Fact]
    public async Task Update_WithRecurrenceTypeAsPerDues_Returns422()
    {
        using var client = WriteClient();
        var created = await TestRequests.CreateAsync(client);
        var id = created.GetProperty("standingInstructionId").GetString();

        using var response = await client.PutAsJsonAsync(
            $"/v1/standing-instructions/{id}", UpdateRequest(recurrenceType: "AsPerDues"), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithAnUnrecognizedEnumString_Returns400()
    {
        using var client = WriteClient();
        var created = await TestRequests.CreateAsync(client);
        var id = created.GetProperty("standingInstructionId").GetString();

        using var response = await client.PutAsJsonAsync(
            $"/v1/standing-instructions/{id}", UpdateRequest(priority: "SuperUrgent"), TestRequests.JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
