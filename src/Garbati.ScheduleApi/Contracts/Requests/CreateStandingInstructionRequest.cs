namespace Garbati.ScheduleApi.Contracts.Requests;

// docs/DESIGN.md Section 2.1/4. Every enum-shaped field is a plain string (Validation/EnumParser) - the
// LedgerApi PR #31 lesson this task was explicitly told not to repeat.
//
// IdempotencyKey: a real gap in docs/DESIGN.md Section 2.1's own original field table, found and fixed
// during this build (docs/DESIGN.md Appendix A) - every other create endpoint on this platform
// (LedgerApi's POST /api/accounts, Garbati.IdentityApi's POST /v1/customers) follows this exact
// convention: a client-supplied key, scoped per calling client, so a network retry transparently
// replays the original resource (StandingInstructionService.CreateAsync) instead of creating a
// duplicate or erroring. The original "no IdempotencyKey field" reading of the design table was wrong,
// not a deliberate design choice this table ever actually made.
public record CreateStandingInstructionRequest(
    string IdempotencyKey,
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
    bool AllowPartialTransfer) : IIdempotentRequest;
