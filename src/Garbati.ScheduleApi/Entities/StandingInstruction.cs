namespace Garbati.ScheduleApi.Entities;

// DESIGN.md Section 2.1. Id (internal PK, never exposed) / StandingInstructionId (generated public id) -
// the same internal-long/public-string split every other GARBATI entity uses (AccountOpening.OperationId,
// LedgerApi's AccountNumber/Reference, IdentityApi's CustomerId/MandateId).
public class StandingInstruction
{
    public long Id { get; set; }
    public string StandingInstructionId { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string CustomerId { get; set; } = null!;

    public AccountType FromAccountType { get; set; }
    public string FromAccountId { get; set; } = null!;
    public AccountType ToAccountType { get; set; }
    public string ToAccountId { get; set; } = null!;

    public TransferType TransferType { get; set; }
    public InstructionType InstructionType { get; set; }
    public Priority Priority { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public string? Narration { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTill { get; set; }

    public RecurrenceType RecurrenceType { get; set; }
    public RecurrenceFrequency RecurrenceFrequency { get; set; }
    public int RecurrenceInterval { get; set; }

    // Day of month (Months/Years) or day of week (Weeks) - null and unused for Days. Clamped, never
    // truncated, to the target month's real length when read by the scheduler (DESIGN.md 3.4, the
    // FINERACT-2914 lesson) - this column stores the caller's configured value as-is.
    public int? RecurrenceOnDay { get; set; }

    public bool AllowPartialTransfer { get; set; }

    public StandingInstructionStatus Status { get; set; }

    // The most recent date a run was ATTEMPTED (success or failure), not "last succeeded" - this is what
    // keeps a same-day second tick a no-op (DESIGN.md 3.1).
    public DateOnly? LastRunDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Idempotency for creation - a gap in docs/DESIGN.md Section 2.1's own original field table, found
    // during the build and fixed here (docs/DESIGN.md Appendix A): every other create endpoint on this
    // platform (LedgerApi's AccountService, Garbati.IdentityApi's CustomerService) follows this exact
    // shape - a client-supplied IdempotencyKey, scoped per calling client (InitiatedBy), plus a
    // RequestHash fingerprinting the fields that define what gets created, so a network retry with the
    // same key transparently replays the original resource instead of creating a duplicate or erroring.
    // Required (not nullable), unlike LedgerApi's Account/Garbati.IdentityApi's Customer, whose own
    // equivalent columns stay nullable only for rows created before those columns existed (seed data) -
    // ScheduleApi is a brand-new service with zero existing rows, so that reason never applies here and
    // every row created through StandingInstructionsController.Create always sets all three.
    public string IdempotencyKey { get; set; } = null!;
    public string InitiatedBy { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
}
