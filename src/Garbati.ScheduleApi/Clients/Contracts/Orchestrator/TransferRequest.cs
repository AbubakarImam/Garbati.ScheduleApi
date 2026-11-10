namespace Garbati.ScheduleApi.Clients.Contracts.Orchestrator;

// Mirrors Garbati.OrchestratorApi.Contracts.Requests.TransferRequest exactly (read directly from that
// repo's own source, not guessed - task file instruction). docs/DESIGN.md 3.1/Section 4: RequestedBy is
// the instruction's own CustomerId, DebitAccountId/CreditAccountId are FromAccountId/ToAccountId.
public record TransferRequest(
    string IdempotencyKey,
    string RequestedBy,
    string DebitAccountId,
    string CreditAccountId,
    decimal Amount,
    string Currency,
    string? Narration);
