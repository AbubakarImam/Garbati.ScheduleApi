namespace Garbati.ScheduleApi.Contracts.Requests;

// docs/DESIGN.md Section 2.1/4. Every enum-shaped field is a plain string (Validation/EnumParser) - the
// LedgerApi PR #31 lesson this task was explicitly told not to repeat. No IdempotencyKey field: unlike
// every other create-style endpoint on this platform, docs/DESIGN.md's own StandingInstruction entity
// table (Section 2.1) has no such field - creating a standing instruction is not itself an idempotent
// operation in this design; only each individual scheduled RUN is (via the sched:{id}:{date} key,
// Section 3.1), which this service generates itself and a caller never supplies.
public record CreateStandingInstructionRequest(
    string Name,
    string CustomerId,
    string FromAccountType,
    string FromAccountId,
    string ToAccountType,
    string ToAccountId,
    string TransferType,
    string InstructionType,
    string Priority,
    decimal Amount,
    string Currency,
    string? Narration,
    DateOnly ValidFrom,
    DateOnly? ValidTill,
    string RecurrenceType,
    string RecurrenceFrequency,
    int RecurrenceInterval,
    int? RecurrenceOnDay,
    bool AllowPartialTransfer);
