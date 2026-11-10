using System.Net;
using Garbati.ScheduleApi.Clients.Contracts.Orchestrator;
using Garbati.ScheduleApi.Clients.Interfaces;
using Garbati.ScheduleApi.Exceptions;

namespace Garbati.ScheduleApi.Clients.Implementations;

// Talks to OrchestratorApi's existing, real POST /v1/transfers over HTTP. The HttpClient is configured in
// Program.cs with this service's own X-Api-Key and base address, from DownstreamServicesOptions.
public class OrchestratorApiClient(HttpClient httpClient) : IOrchestratorApiClient
{
    public async Task<TransferResultResponse> SubmitTransferAsync(TransferRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("v1/transfers", request, cancellationToken);

        // OrchestratorApi's TransfersController returns a real TransferResultResponse body on 200 AND on
        // 422 (its own comment: "LedgerApi having posted the leg as Failed is a business-rule failure,
        // returned with 422, not a fabricated 200"). But a 422 can ALSO be an exception-mapped
        // ProblemDetails (AuthorizationDeniedException, thrown before any TransferResultResponse exists)
        // - DownstreamResponseReader.IsProblemDetails is what tells the two 422 shapes apart; checking
        // status code alone would silently deserialize a ProblemDetails body into a TransferResultResponse
        // with every field null (exactly the LedgerApi PR #31 class of mistake this task was told to avoid
        // repeating, applied here to content-type instead of enum shape).
        var isProblem = DownstreamResponseReader.IsProblemDetails(response);
        if (!isProblem && (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.UnprocessableEntity))
        {
            return await response.Content.ReadFromJsonAsync<TransferResultResponse>(cancellationToken)
                ?? throw new DownstreamApiException("OrchestratorApi", (int)response.StatusCode, "OrchestratorApi POST /v1/transfers: empty response body.");
        }

        var problem = await DownstreamResponseReader.ReadProblemAsync(response, cancellationToken);
        throw new DownstreamApiException(
            "OrchestratorApi", (int)response.StatusCode,
            $"OrchestratorApi POST /v1/transfers: {DownstreamResponseReader.DescribeFailure(problem, response)}");
    }
}
