using Garbati.ScheduleApi.Clients.Contracts.Ledger;
using Garbati.ScheduleApi.Clients.Contracts.Orchestrator;
using Garbati.ScheduleApi.Clients.Interfaces;
using Garbati.ScheduleApi.Data;
using Garbati.ScheduleApi.Entities;
using Garbati.ScheduleApi.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Garbati.ScheduleApi.Scheduling;

// docs/DESIGN.md Section 3. One tick's worth of work: find every Active instruction due today that
// hasn't already run today, submit each to OrchestratorApi (with the narrow LedgerApi balance check when
// AllowPartialTransfer is opted in), and record the outcome. Per-instruction isolation (docs/DESIGN.md
// 3.3's own citation of Fineract's PR #6806 bug fix): one instruction throwing an unexpected exception
// must never stop the rest of the tick from running.
public class StandingInstructionRunner(
    ScheduleDbContext dbContext,
    IOrchestratorApiClient orchestratorApiClient,
    ILedgerApiClient ledgerApiClient,
    ILogger<StandingInstructionRunner> logger) : IStandingInstructionRunner
{
    public async Task RunDueInstructionsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        // docs/DESIGN.md 2.1/Section 5: Priority is stored, not enforced for ordering - a simple,
        // deterministic order (ascending internal Id) is used instead of inventing a guarantee neither
        // this design nor Fineract's own confirmed behavior backs up.
        var candidates = await dbContext.StandingInstructions
            .Where(s => s.Status == StandingInstructionStatus.Active)
            .Where(s => s.ValidFrom <= today)
            .Where(s => s.ValidTill == null || s.ValidTill >= today)
            .Where(s => s.LastRunDate == null || s.LastRunDate < today)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);

        foreach (var instruction in candidates)
        {
            if (!RecurrenceSchedule.IsDue(instruction, today))
            {
                continue;
            }

            try
            {
                await RunOneAsync(instruction, today, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unexpected error running standing instruction {StandingInstructionId} for {ScheduledDate}",
                    instruction.StandingInstructionId, today);
            }
        }
    }

    private async Task RunOneAsync(StandingInstruction instruction, DateOnly today, CancellationToken cancellationToken)
    {
        // docs/DESIGN.md 3.1: the internal numeric Id, not the public StandingInstructionId string - see
        // Entities/StandingInstructionRun.cs's comment on why that mirrors LedgerApi's own
        // "chain:{chain.Id}:{sequence}" precedent exactly.
        var idempotencyKey = $"sched:{instruction.Id}:{today:yyyy-MM-dd}";
        var amountToSubmit = instruction.Amount;

        if (instruction.AllowPartialTransfer)
        {
            // docs/DESIGN.md 3.2 (D-33 decision 4): this call is deliberately narrow - it only ever runs
            // for the opt-in case, never when AllowPartialTransfer is false.
            BalanceResponse? balance;
            try
            {
                balance = await ledgerApiClient.GetBalanceAsync(instruction.FromAccountId, cancellationToken);
            }
            catch (DownstreamApiException ex)
            {
                await RecordRunAsync(instruction, today, idempotencyKey, StandingInstructionRunStatus.Failed, 0m, ex.Message, null, cancellationToken);
                return;
            }

            var realBalance = balance?.Balance ?? 0m;
            if (realBalance <= 0m)
            {
                await RecordRunAsync(
                    instruction, today, idempotencyKey, StandingInstructionRunStatus.Skipped, 0m,
                    $"AllowPartialTransfer: '{instruction.FromAccountId}' has no available balance (real balance {realBalance} {instruction.Currency}). Nothing was submitted.",
                    null, cancellationToken);
                return;
            }

            if (realBalance < instruction.Amount)
            {
                amountToSubmit = realBalance;
            }
        }

        var isPartial = amountToSubmit < instruction.Amount;

        try
        {
            var result = await orchestratorApiClient.SubmitTransferAsync(
                new TransferRequest(
                    idempotencyKey, instruction.CustomerId, instruction.FromAccountId, instruction.ToAccountId,
                    amountToSubmit, instruction.Currency, instruction.Narration),
                cancellationToken);

            if (result.Status == "Failed")
            {
                await RecordRunAsync(
                    instruction, today, idempotencyKey, StandingInstructionRunStatus.Failed, amountToSubmit,
                    result.FailureReason, result.Reference, cancellationToken);
                return;
            }

            var status = isPartial ? StandingInstructionRunStatus.Partial : StandingInstructionRunStatus.Success;
            var errorMessage = isPartial
                ? $"AllowPartialTransfer: submitted {amountToSubmit} {instruction.Currency} of the configured {instruction.Amount} {instruction.Currency}."
                : null;
            await RecordRunAsync(instruction, today, idempotencyKey, status, amountToSubmit, errorMessage, result.Reference, cancellationToken);
        }
        catch (DownstreamApiException ex)
        {
            // docs/DESIGN.md 3.3 (D-33 decision 2): recorded and left alone - NO retry. The next attempt
            // is the instruction's own next naturally-scheduled occurrence, never a second attempt this
            // same tick or a later tick the same day (LastRunDate, updated below regardless of outcome,
            // is what the WHERE clause above uses to exclude this instruction for the rest of today).
            await RecordRunAsync(instruction, today, idempotencyKey, StandingInstructionRunStatus.Failed, amountToSubmit, ex.Message, null, cancellationToken);
        }
    }

    private async Task RecordRunAsync(
        StandingInstruction instruction, DateOnly today, string idempotencyKey, StandingInstructionRunStatus status,
        decimal amount, string? errorMessage, string? orchestratorReference, CancellationToken cancellationToken)
    {
        dbContext.StandingInstructionRuns.Add(new StandingInstructionRun
        {
            StandingInstructionId = instruction.Id,
            ScheduledDate = today,
            Status = status,
            Amount = amount,
            ExecutedAt = DateTimeOffset.UtcNow,
            ErrorMessage = errorMessage,
            IdempotencyKey = idempotencyKey,
            OrchestratorReference = orchestratorReference,
        });

        // docs/DESIGN.md 3.1/Section 5: updated regardless of outcome, so a same-day second tick (a
        // restart, a double trigger) is a no-op for this instruction, and a Failed run is never
        // re-attempted before its own next naturally-scheduled occurrence.
        instruction.LastRunDate = today;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The run-history table's own unique index on IdempotencyKey (defence in depth, docs/DESIGN.md
            // 3.1) may have rejected a second insert for the same instruction/date - another tick (e.g. a
            // restart mid-tick, before LastRunDate's own update above was ever persisted) could already
            // have recorded this exact run. Confirmed, not assumed: a save can also fail for an unrelated
            // reason (a transient connection drop), which must still surface as a real failure, not be
            // swallowed as "someone else already did this."
            dbContext.ChangeTracker.Clear();
            var alreadyRecorded = await dbContext.StandingInstructionRuns.AsNoTracking()
                .AnyAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
            if (!alreadyRecorded)
            {
                throw;
            }
        }
    }
}
