namespace Garbati.ScheduleApi.Clients.Contracts.Orchestrator;

// Mirrors Garbati.OrchestratorApi.Contracts.Responses.TransferResultResponse exactly. Status is "Failed"
// for a business-rule failure (e.g. LedgerApi declined for insufficient funds) - OrchestratorApi's own
// TransfersController returns that case as 422 WITH this body, not as a thrown exception, so
// OrchestratorApiClient.SubmitTransferAsync below must read a 422 response as data in that case, exactly
// like Garbati.OrchestratorApi.Clients.Implementations.LedgerApiClient.TransferAsync's own comment
// explains for the equivalent LedgerApi case.
public record TransferResultResponse(
    string DecisionId,
    string Reference,
    string Status,
    string? FailureReason);
