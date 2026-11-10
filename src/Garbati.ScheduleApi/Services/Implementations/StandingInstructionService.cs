using Garbati.ScheduleApi.Contracts.Requests;
using Garbati.ScheduleApi.Contracts.Responses;
using Garbati.ScheduleApi.Data;
using Garbati.ScheduleApi.Entities;
using Garbati.ScheduleApi.Exceptions;
using Garbati.ScheduleApi.Validation;
using Microsoft.EntityFrameworkCore;

namespace Garbati.ScheduleApi.Services.Implementations;

// docs/DESIGN.md Section 4. Shape validation (CreateStandingInstructionRequestValidator etc.) has
// already run by the time any method here is called - this class owns the business rules that need a
// clear, specific 422 (NoFinancingModuleException) rather than a generic 400, plus all persistence.
public class StandingInstructionService(ScheduleDbContext dbContext) : IStandingInstructionService
{
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;

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
        };

        dbContext.StandingInstructions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(entity);
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
