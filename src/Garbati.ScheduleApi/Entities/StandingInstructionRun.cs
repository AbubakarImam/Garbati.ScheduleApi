namespace Garbati.ScheduleApi.Entities;

// DESIGN.md Section 2.2. StandingInstructionId is the INTERNAL numeric FK (DESIGN.md's own table types it
// `long`, not the public StandingInstructionId string) - the idempotency key below is derived from this
// same internal id, mirroring LedgerApi's own precedent exactly: ChainService generates leg keys as
// "chain:{chain.Id}:{sequence}" using TransactionChain's internal PK, not its public Reference. An
// internally-generated, caller-unreachable dedup key is not the same kind of value as a public-facing
// identifier (AccountOpening.OperationId, LedgerApi's AccountNumber/Reference) - this is the platform's
// own established exception to "never expose the internal Id," not a new one invented here.
public class StandingInstructionRun
{
    public long Id { get; set; }
    public long StandingInstructionId { get; set; }

    // The date this run was due for - half of the idempotency key, and distinct from ExecutedAt (when the
    // scheduler actually attempted it, which could in principle be a later tick on the same day or, after
    // a restart, a later calendar day for a backlog entry - v1's hourly poll with LastRunDate gating never
    // produces a backlog in practice, but ScheduledDate and ExecutedAt are still conceptually different
    // things).
    public DateOnly ScheduledDate { get; set; }

    public StandingInstructionRunStatus Status { get; set; }

    // The amount actually transferred - may be less than StandingInstruction.Amount on a Partial run.
    public decimal Amount { get; set; }

    public DateTimeOffset ExecutedAt { get; set; }

    // Set on Failed; also set, non-fatally, on a Partial run's underlying "funds were short" reason, and
    // on Skipped explaining why nothing was submitted.
    public string? ErrorMessage { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    // OrchestratorApi's own transfer reference, for traceability - null on a Skipped run (nothing was
    // ever submitted).
    public string? OrchestratorReference { get; set; }
}
