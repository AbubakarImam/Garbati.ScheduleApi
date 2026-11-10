using Garbati.ScheduleApi.Clients.Contracts.Orchestrator;

namespace Garbati.ScheduleApi.Clients.Interfaces;

// ScheduleApi as a client of OrchestratorApi's existing, real POST /v1/transfers (docs/DESIGN.md 3.1 -
// "the same call any channel makes"). No new endpoint, no contract change on OrchestratorApi's side.
public interface IOrchestratorApiClient
{
    Task<TransferResultResponse> SubmitTransferAsync(TransferRequest request, CancellationToken cancellationToken);
}
