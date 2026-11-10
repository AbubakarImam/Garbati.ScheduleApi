using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Garbati.ScheduleApi.Authorization;
using Garbati.ScheduleApi.Contracts.Requests;
using Garbati.ScheduleApi.Contracts.Responses;
using Garbati.ScheduleApi.Data;
using Garbati.ScheduleApi.Entities;
using Garbati.ScheduleApi.Exceptions;
using Garbati.ScheduleApi.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Garbati.ScheduleApi.Services.Implementations;

// docs/DESIGN.md Section 4. Shape validation (CreateStandingInstructionRequestValidator etc.) has
// already run by the time any method here is called - this class owns the business rules that need a
// clear, specific 422 (NoFinancingModuleException) rather than a generic 400, plus all persistence.
public class StandingInstructionService(ScheduleDbContext dbContext, ICurrentClient currentClient) : IStandingInstructionService
{
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;
    private const int MaxIdGenerationAttempts = 5;

    // Fingerprint of the fields that define the standing instruction being created, so a reused
    // idempotency key with a different body is detected (mirrors LedgerApi's AccountService.HashRequest /
    // Garbati.IdentityApi's CustomerService.HashRequest exactly). Includes every field that determines
    // what the resulting instruction actually does, not just its "primary" identity fields - the account
    // types/transfer type/instruction type only ever pass validation as one value each in v1, but a
    // future caller sending a DIFFERENT (also-invalid) value under the same key should still be treated
    // as a genuinely different request, not silently matched against the first attempt. Excluded as pure
    // metadata with no effect on what gets created or how it behaves: Narration (the same exclusion
    // reasoning as decision #44's narration-exclusion precedent - free text, passed through unchanged to
    // every run) and Priority (docs/DESIGN.md Section 5: "stored, not enforced" - advisory only in v1).
    private static string HashRequest(CreateStandingInstructionRequest request)
    {
        var fields = JsonSerializer.Serialize(new object?[]
        {
            request.Name,
            request.CustomerId,
            EnumParser.Parse<AccountType>(request.FromAccountType)?.ToString(),
            request.FromAccountId,
            EnumParser.Parse<AccountType>(request.ToAccountType)?.ToString(),
            request.ToAccountId,
            EnumParser.Parse<TransferType>(request.TransferType)?.ToString(),
            EnumParser.Parse<InstructionType>(request.InstructionType)?.ToString(),
            request.Amount,
            request.Currency,
            request.ValidFrom,
            request.ValidTill,
            EnumParser.Parse<RecurrenceType>(request.RecurrenceType)?.ToString(),
            EnumParser.Parse<RecurrenceFrequency>(request.RecurrenceFrequency)?.ToString(),
            request.RecurrenceInterval,
            request.RecurrenceOnDay,
            request.AllowPartialTransfer,
        });
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(fields)));
    }

    public async Task<StandingInstructionResponse> CreateAsync(
        CreateStandingInstructionRequest request, CancellationToken cancellationToken)
    {
        var fromAccountType = EnumParser.Parse<AccountType>(request.FromAccountType)!.Value;
        var toAccountType = EnumParser.Parse<AccountType>(request.ToAccountType)!.Value;
        var transferType = EnumParser.Parse<TransferType>(request.TransferType)!.Value;
        var instructionType = EnumParser.Parse<InstructionType>(request.InstructionType)!.Value;
        var recurrenceType = EnumParser.Parse<RecurrenceType>(request.RecurrenceType)!.Value;

        // docs/DESIGN.md Section 2.1/4: every Fineract axis that needs a loan is modeled but rejected
        // clearly, with the specific field named, until a financing module exists.
        if (fromAccountType == AccountType.Loan)
            throw new NoFinancingModuleException("FromAccountType 'Loan' requires a financing module, which does not exist yet.");
        if (toAccountType == AccountType.Loan)
            throw new NoFinancingModuleException("ToAccountType 'Loan' requires a financing module, which does not exist yet.");
        if (transferType != TransferType.AccountTransfer)
            throw new NoFinancingModuleException($"TransferType '{transferType}' requires a financing module, which does not exist yet. Only AccountTransfer is supported today.");
        if (instructionType != InstructionType.Fixed)
            throw new NoFinancingModuleException($"InstructionType '{instructionType}' requires a financing module, which does not exist yet. Only Fixed is supported today.");
        if (recurrenceType != RecurrenceType.Periodic)
            throw new NoFinancingModuleException($"RecurrenceType '{recurrenceType}' requires a financing module, which does not exist yet. Only Periodic is supported today.");

        var clientId = currentClient.ClientId;
        var requestHash = HashRequest(request);

        // Checked BEFORE the Name-uniqueness pre-check below, not after: on a genuine replay (same key,
        // same body), request.Name is necessarily the same Name the first, already-successful call used -
        // without this upfront check, the Name pre-check would always fire first and reject every replay
        // as a duplicate name, never reaching the idempotency match at all. LedgerApi's AccountService/
        // Garbati.IdentityApi's CustomerService don't need this extra step because neither has a second,
        // independent uniqueness rule (like this entity's Name) that a legitimate replay would otherwise
        // always collide with.
        var existingByKey = await dbContext.StandingInstructions.AsNoTracking().FirstOrDefaultAsync(
            s => s.InitiatedBy == clientId && s.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existingByKey is not null)
        {
            // Same key, different request: refuse rather than report the old instruction as this one's (409).
            if (existingByKey.RequestHash != requestHash)
                throw new DuplicateIdempotencyKeyException(
                    $"Idempotency key '{request.IdempotencyKey}' was already used for a different request.");

            return ToResponse(existingByKey);
        }

        // Same retry-on-collision shape as LedgerApi's AccountService.CreateAccountAsync / Garbati.
        // IdentityApi's CustomerService.CreateCustomerAsync (confirmed against both repos' real source
        // before copying, not guessed): generate the public id, try to insert, and on a unique-constraint
        // violation branch on WHICH constraint fired rather than assuming it was the id. The upfront check
        // above handles the common case; this catch is the defence-in-depth fallback for a genuine race
        // (two concurrent requests with the same new key, both passing the upfront check before either
        // has inserted).
        for (var attempt = 1; attempt <= MaxIdGenerationAttempts; attempt++)
        {
            if (await dbContext.StandingInstructions.AnyAsync(s => s.Name == request.Name, cancellationToken))
            {
                throw new DuplicateStandingInstructionNameException(request.Name);
            }

            var now = DateTimeOffset.UtcNow;
            var entity = new StandingInstruction
            {
                StandingInstructionId = StandingInstructionIdGenerator.Generate(),
                Name = request.Name,
                CustomerId = request.CustomerId,
                FromAccountType = fromAccountType,
                FromAccountId = request.FromAccountId,
                ToAccountType = toAccountType,
                ToAccountId = request.ToAccountId,
                TransferType = transferType,
                InstructionType = instructionType,
                Priority = EnumParser.Parse<Priority>(request.Priority)!.Value,
                Amount = request.Amount,
                Currency = request.Currency,
                Narration = request.Narration,
                ValidFrom = request.ValidFrom,
                ValidTill = request.ValidTill,
                RecurrenceType = recurrenceType,
                RecurrenceFrequency = EnumParser.Parse<RecurrenceFrequency>(request.RecurrenceFrequency)!.Value,
                RecurrenceInterval = request.RecurrenceInterval,
                RecurrenceOnDay = request.RecurrenceOnDay,
                AllowPartialTransfer = request.AllowPartialTransfer,
                Status = StandingInstructionStatus.Active,
                LastRunDate = null,
                CreatedAt = now,
                UpdatedAt = now,
                IdempotencyKey = request.IdempotencyKey,
                InitiatedBy = clientId,
                RequestHash = requestHash,
            };

            dbContext.StandingInstructions.Add(entity);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return ToResponse(entity);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } pgEx)
            {
                // Detach, or the retry below would try to save this attempt's row a second time alongside
                // the next attempt's.
                dbContext.Entry(entity).State = EntityState.Detached;

                if (pgEx.ConstraintName == "ix_standing_instructions_initiated_by_idempotency_key")
                {
                    var existing = await dbContext.StandingInstructions.AsNoTracking().FirstOrDefaultAsync(
                        s => s.InitiatedBy == clientId && s.IdempotencyKey == request.IdempotencyKey, cancellationToken);

                    if (existing is null)
                        throw new InvalidOperationException("Error retrieving the result of the existing idempotency key.");

                    // Same key, different request: refuse rather than report the old instruction as this
                    // one's (409).
                    if (existing.RequestHash != requestHash)
                        throw new DuplicateIdempotencyKeyException(
                            $"Idempotency key '{request.IdempotencyKey}' was already used for a different request.");

                    return ToResponse(existing);
                }

                if (pgEx.ConstraintName == "ix_standing_instructions_name")
                {
                    // A concurrent request created the same Name between this attempt's own pre-check
                    // above and its insert - the pre-check is an optimization for the common case, this
                    // catch is what actually guarantees the constraint under a real race.
                    throw new DuplicateStandingInstructionNameException(request.Name);
                }

                // Otherwise the collision was on StandingInstructionId (ix_standing_instructions_standing_
                // instruction_id): loop around and generate another one.
            }
        }

        throw new InvalidOperationException($"Could not allocate a unique StandingInstructionId after {MaxIdGenerationAttempts} attempts.");
    }

    public async Task<StandingInstructionResponse?> GetAsync(string standingInstructionId, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(standingInstructionId, cancellationToken);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<PagedResponse<StandingInstructionResponse>> ListAsync(
        string customerId, int pageSize, string? pageToken, CancellationToken cancellationToken)
    {
        pageSize = Math.Clamp(pageSize, MinPageSize, MaxPageSize);

        var query = dbContext.StandingInstructions.AsNoTracking().Where(s => s.CustomerId == customerId);
        if (PageToken.TryDecode(pageToken, out var after))
        {
            query = query.Where(s => s.Id > after);
        }

        var rows = await query.OrderBy(s => s.Id).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;

        return new PagedResponse<StandingInstructionResponse>(
            page.Select(ToResponse).ToList(),
            hasMore ? PageToken.Encode(page[^1].Id) : null);
    }

    public async Task<StandingInstructionResponse> UpdateAsync(
        string standingInstructionId, UpdateStandingInstructionRequest request, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(standingInstructionId, cancellationToken)
            ?? throw new StandingInstructionNotFoundException(standingInstructionId);

        if (entity.Status == StandingInstructionStatus.Deleted)
        {
            throw new StandingInstructionDeletedException(standingInstructionId);
        }

        var recurrenceType = EnumParser.Parse<RecurrenceType>(request.RecurrenceType)!.Value;
        if (recurrenceType != RecurrenceType.Periodic)
        {
            throw new NoFinancingModuleException($"RecurrenceType '{recurrenceType}' requires a financing module, which does not exist yet. Only Periodic is supported today.");
        }

        entity.Amount = request.Amount;
        entity.ValidFrom = request.ValidFrom;
        entity.ValidTill = request.ValidTill;
        entity.RecurrenceType = recurrenceType;
        entity.RecurrenceFrequency = EnumParser.Parse<RecurrenceFrequency>(request.RecurrenceFrequency)!.Value;
        entity.RecurrenceInterval = request.RecurrenceInterval;
        entity.RecurrenceOnDay = request.RecurrenceOnDay;
        entity.Priority = EnumParser.Parse<Priority>(request.Priority)!.Value;
        entity.AllowPartialTransfer = request.AllowPartialTransfer;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(entity);
    }

    public async Task<StandingInstructionResponse> DeleteAsync(string standingInstructionId, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(standingInstructionId, cancellationToken)
            ?? throw new StandingInstructionNotFoundException(standingInstructionId);

        // Soft delete (docs/DESIGN.md Section 4) - idempotent: deleting an already-Deleted instruction is
        // a harmless no-op, not an error, same as Fineract's own real mechanism this mirrors.
        entity.Status = StandingInstructionStatus.Deleted;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(entity);
    }

    public async Task<StandingInstructionResponse> UpdateStatusAsync(
        string standingInstructionId, UpdateStandingInstructionStatusRequest request, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(standingInstructionId, cancellationToken)
            ?? throw new StandingInstructionNotFoundException(standingInstructionId);

        if (entity.Status == StandingInstructionStatus.Deleted)
        {
            throw new StandingInstructionDeletedException(standingInstructionId);
        }

        // UpdateStandingInstructionStatusRequestValidator already confirmed this parses to Active or
        // Disabled - never Deleted, never an unrecognized string.
        entity.Status = EnumParser.Parse<StandingInstructionStatus>(request.Status)!.Value;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(entity);
    }

    public async Task<PagedResponse<StandingInstructionRunResponse>?> ListRunsAsync(
        string standingInstructionId, int pageSize, string? pageToken, CancellationToken cancellationToken)
    {
        var entity = await FindAsync(standingInstructionId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        pageSize = Math.Clamp(pageSize, MinPageSize, MaxPageSize);

        // Most-recent-first: a run history reads like a ledger statement, newest at the top (unlike
        // Garbati.IdentityApi's ListPendingRequestsAsync, which is oldest-first because that is a queue to
        // be worked through in order - a run history is read, not worked).
        var query = dbContext.StandingInstructionRuns.AsNoTracking()
            .Where(r => r.StandingInstructionId == entity.Id);
        if (PageToken.TryDecode(pageToken, out var after))
        {
            query = query.Where(r => r.Id < after);
        }

        var rows = await query.OrderByDescending(r => r.Id).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;

        return new PagedResponse<StandingInstructionRunResponse>(
            page.Select(ToResponse).ToList(),
            hasMore ? PageToken.Encode(page[^1].Id) : null);
    }

    private Task<StandingInstruction?> FindAsync(string standingInstructionId, CancellationToken cancellationToken) =>
        dbContext.StandingInstructions.FirstOrDefaultAsync(s => s.StandingInstructionId == standingInstructionId, cancellationToken);

    private static StandingInstructionResponse ToResponse(StandingInstruction entity) => new(
        entity.StandingInstructionId,
        entity.Name,
        entity.CustomerId,
        entity.FromAccountType.ToString(),
        entity.FromAccountId,
        entity.ToAccountType.ToString(),
        entity.ToAccountId,
        entity.TransferType.ToString(),
        entity.InstructionType.ToString(),
        entity.Priority.ToString(),
        entity.Amount,
        entity.Currency,
        entity.Narration,
        entity.ValidFrom,
        entity.ValidTill,
        entity.RecurrenceType.ToString(),
        entity.RecurrenceFrequency.ToString(),
        entity.RecurrenceInterval,
        entity.RecurrenceOnDay,
        entity.AllowPartialTransfer,
        entity.Status.ToString(),
        entity.LastRunDate,
        entity.CreatedAt,
        entity.UpdatedAt);

    private static StandingInstructionRunResponse ToResponse(StandingInstructionRun run) => new(
        run.ScheduledDate,
        run.Status.ToString(),
        run.Amount,
        run.ExecutedAt,
        run.ErrorMessage,
        run.IdempotencyKey,
        run.OrchestratorReference);
}
