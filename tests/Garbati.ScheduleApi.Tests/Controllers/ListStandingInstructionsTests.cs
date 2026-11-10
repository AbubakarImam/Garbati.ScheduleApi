using Garbati.ScheduleApi.Authorization;
using System.Net;
using System.Net.Http.Json;
using Garbati.ScheduleApi.Tests.Fixtures;

namespace Garbati.ScheduleApi.Tests.Controllers;

// GET /v1/standing-instructions?customerId= (docs/DESIGN.md Section 4) - paged list.
[Collection("Api")]
public class ListStandingInstructionsTests(ApiFixture fixture)
{
    private HttpClient Client() => fixture.CreateClient(ApiFixture.OnlyClient(ScheduleApiScopes.Read));
    private HttpClient WriteClient() => fixture.CreateClient(ApiFixture.OnlyClient(ScheduleApiScopes.Write));

    [Fact]
    public async Task List_ReturnsOnlyInstructionsForTheGivenCustomer_Paged()
    {
        var customerId = $"CUS-{Guid.NewGuid():N}";
        using var write = WriteClient();
        await TestRequests.CreateAsync(write, TestRequests.CreateRequest(customerId: customerId));
        await TestRequests.CreateAsync(write, TestRequests.CreateRequest(customerId: customerId));
        await TestRequests.CreateAsync(write); // a different, random customer - must not appear.

        using var read = Client();
        using var response = await read.GetAsync($"/v1/standing-instructions?customerId={customerId}&pageSize=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var items = body.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("nextPageToken").GetString()));

        var nextToken = body.GetProperty("nextPageToken").GetString();
        using var secondPage = await read.GetAsync($"/v1/standing-instructions?customerId={customerId}&pageSize=1&pageToken={nextToken}");
        var secondBody = await secondPage.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(1, secondBody.GetProperty("items").GetArrayLength());
        Assert.True(secondBody.GetProperty("nextPageToken").ValueKind is System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task List_WithoutACustomerId_Returns400()
    {
        using var client = Client();

        using var response = await client.GetAsync("/v1/standing-instructions");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
