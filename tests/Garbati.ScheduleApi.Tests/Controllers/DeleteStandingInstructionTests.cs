using Garbati.ScheduleApi.Authorization;
using System.Net;
using System.Net.Http.Json;
using Garbati.ScheduleApi.Tests.Fixtures;

namespace Garbati.ScheduleApi.Tests.Controllers;

// DELETE /v1/standing-instructions/{id} (docs/DESIGN.md Section 4) - soft delete, Status -> Deleted.
[Collection("Api")]
public class DeleteStandingInstructionTests(ApiFixture fixture)
{
    private HttpClient WriteClient() => fixture.CreateClient(ApiFixture.OnlyClient(ScheduleApiScopes.Write));
    private HttpClient ReadClient() => fixture.CreateClient(ApiFixture.OnlyClient(ScheduleApiScopes.Read));

    [Fact]
    public async Task Delete_SetsStatusToDeleted_AndTheRowIsStillReadableAfterwards()
    {
        using var write = WriteClient();
        var created = await TestRequests.CreateAsync(write);
        var id = created.GetProperty("standingInstructionId").GetString();

        using var deleteResponse = await write.DeleteAsync($"/v1/standing-instructions/{id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        var deleteBody = await deleteResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("Deleted", deleteBody.GetProperty("status").GetString());

        // Soft delete, not a row removal (docs/DESIGN.md Section 4) - GET still finds it.
        using var read = ReadClient();
        using var getResponse = await read.GetAsync($"/v1/standing-instructions/{id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getBody = await getResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("Deleted", getBody.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Delete_ForAnUnknownId_Returns404()
    {
        using var client = WriteClient();

        using var response = await client.DeleteAsync("/v1/standing-instructions/SI-DOESNOTEXIST");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_IsIdempotent_DeletingAnAlreadyDeletedInstructionIsANoOp()
    {
        using var client = WriteClient();
        var created = await TestRequests.CreateAsync(client);
        var id = created.GetProperty("standingInstructionId").GetString();

        await client.DeleteAsync($"/v1/standing-instructions/{id}");
        using var secondDelete = await client.DeleteAsync($"/v1/standing-instructions/{id}");

        Assert.Equal(HttpStatusCode.OK, secondDelete.StatusCode);
    }
}
