namespace Garbati.ScheduleApi.Contracts.Responses;

// docs/DESIGN.md Section 2.1, as plain strings for every enum field (Validation/EnumParser's mirror
// image: Entities.AccountType.Savings.ToString() == "Savings", so there is no separate "format" step
// beyond ToString() - the parse direction is what needs the explicit helper, not this one).
public record StandingInstructionResponse(
    string StandingInstructionId,
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
    bool AllowPartialTransfer,
    string Status,
    DateOnly? LastRunDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
