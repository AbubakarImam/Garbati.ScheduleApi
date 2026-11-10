using System.Net;
using Garbati.ScheduleApi.Clients.Implementations;
using Garbati.ScheduleApi.Exceptions;

namespace Garbati.ScheduleApi.Tests.Clients;

public class LedgerApiClientTests
{
    private static LedgerApiClient CreateClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://ledger.invalid") });

    [Fact]
    public async Task GetBalanceAsync_On200_ReturnsTheBalance()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
            """{"accountNumber":"1000000001","balance":250.50,"currencyCode":"NGN","asOf":"2026-03-10T00:00:00Z"}""");
        var client = CreateClient(handler);

        var result = await client.GetBalanceAsync("1000000001", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(250.50m, result!.Balance);
    }

    [Fact]
    public async Task GetBalanceAsync_On404_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.NotFound, null);
        var client = CreateClient(handler);

        var result = await client.GetBalanceAsync("DOES-NOT-EXIST", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetBalanceAsync_On500_Throws()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError,
            """{"title":"An unexpected error occurred."}""", "application/problem+json");
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<DownstreamApiException>(() => client.GetBalanceAsync("1000000001", CancellationToken.None));
    }

    [Fact]
    public async Task GetBalanceAsync_SendsTheRequestToTheExpectedPath()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
            """{"accountNumber":"1000000001","balance":250.50,"currencyCode":"NGN","asOf":"2026-03-10T00:00:00Z"}""");
        var client = CreateClient(handler);

        await client.GetBalanceAsync("1000000001", CancellationToken.None);

        Assert.Equal("api/accounts/1000000001/balance", handler.LastRequest!.RequestUri!.AbsolutePath.TrimStart('/'));
    }
}
