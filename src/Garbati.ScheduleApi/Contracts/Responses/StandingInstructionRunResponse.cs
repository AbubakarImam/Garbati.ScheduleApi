namespace Garbati.ScheduleApi.Contracts.Responses;

// docs/DESIGN.md Section 2.2. Deliberately does NOT include the internal StandingInstructionId FK
// (long) - a caller reaches run history only through GET /v1/standing-instructions/{id}/runs, scoped by
// the PUBLIC StandingInstructionId already in the URL, so the internal numeric FK has no reason to be
// repeated in the body. IdempotencyKey IS included (it is useful, caller-facing traceability, same as
// OrchestratorReference) even though it is built from that same internal numeric id - see
// Entities/StandingInstructionRun.cs's comment on why that is not the same kind of exposure.
public record StandingInstructionRunResponse(
    DateOnly ScheduledDate,
    string Status,
    decimal Amount,
    DateTimeOffset ExecutedAt,
    string? ErrorMessage,
    string IdempotencyKey,
    string? OrchestratorReference);
