using System.Net;
using Garbati.ScheduleApi.Clients.Contracts.Ledger;
using Garbati.ScheduleApi.Clients.Interfaces;
using Garbati.ScheduleApi.Exceptions;

namespace Garbati.ScheduleApi.Clients.Implementations;

// Talks to LedgerApi's existing, real GET /api/accounts/{accountNumber}/balance over HTTP - the one,
// narrow call ScheduleApi ever makes directly to LedgerApi (docs/DESIGN.md 3.2, D-33 decision 4).
public class LedgerApiClient(HttpClient httpClient) : ILedgerApiClient
{
    public async Task<BalanceResponse?> GetBalanceAsync(string accountNumber, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"api/accounts/{Uri.EscapeDataString(accountNumber)}/balance", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<BalanceResponse>(cancellationToken)
                ?? throw new DownstreamApiException("LedgerApi", (int)response.StatusCode, "LedgerApi GET .../balance: empty response body.");
        }

        var problem = await DownstreamResponseReader.ReadProblemAsync(response, cancellationToken);
        throw new DownstreamApiException(
            "LedgerApi", (int)response.StatusCode,
            $"LedgerApi GET .../balance: {DownstreamResponseReader.DescribeFailure(problem, response)}");
    }
}
