using Garbati.ScheduleApi.Authorization;
using System.Net;
using System.Net.Http.Json;
using Garbati.ScheduleApi.Tests.Fixtures;

namespace Garbati.ScheduleApi.Tests.Controllers;

// GET /v1/standing-instructions/{id} (docs/DESIGN.md Section 4).
[Collection("Api")]
public class GetStandingInstructionTests(ApiFixture fixture)
{
    private HttpClient Client() => fixture.CreateClient(ApiFixture.OnlyClient(ScheduleApiScopes.Read));
    private HttpClient WriteClient() => fixture.CreateClient(ApiFixture.OnlyClient(ScheduleApiScopes.Write));

    [Fact]
    public async Task GetById_ForAnExistingInstruction_Returns200_WithTheFullResponseShape()
    {
        using var write = WriteClient();
        var created = await TestRequests.CreateAsync(write);
        var id = created.GetProperty("standingInstructionId").GetString();

        using var read = Client();
        using var response = await read.GetAsync($"/v1/standing-instructions/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(id, body.GetProperty("standingInstructionId").GetString());
    }

    [Fact]
    public async Task GetById_ForAnUnknownId_Returns404()
    {
        using var client = Client();

        using var response = await client.GetAsync("/v1/standing-instructions/SI-DOESNOTEXIST");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
