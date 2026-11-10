using Garbati.ScheduleApi.Clients.Contracts.Ledger;
using Garbati.ScheduleApi.Entities;
using Garbati.ScheduleApi.Scheduling;
using Garbati.ScheduleApi.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Garbati.ScheduleApi.Tests.Scheduling;

// docs/DESIGN.md Section 3 end to end, against a real Postgres database (so the unique IdempotencyKey
// index is real) with fake OrchestratorApi/LedgerApi clients (no real network call).
[Collection("Postgres")]
public class StandingInstructionRunnerTests(PostgresFixture fixture) : IAsyncLifetime
{
    private FakeOrchestratorApiClient _orchestratorApiClient = null!;
    private FakeLedgerApiClient _ledgerApiClient = null!;

    public Task InitializeAsync()
    {
        _orchestratorApiClient = new FakeOrchestratorApiClient();
        _ledgerApiClient = new FakeLedgerApiClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await fixture.ResetAsync();

    private StandingInstructionRunner CreateRunner(Garbati.ScheduleApi.Data.ScheduleDbContext dbContext) =>
        new(dbContext, _orchestratorApiClient, _ledgerApiClient, NullLogger<StandingInstructionRunner>.Instance);

    private static StandingInstruction ActiveDailyInstruction(string name, decimal amount = 1000m, bool allowPartial = false) => new()
    {
        StandingInstructionId = $"SI-{Guid.NewGuid():N}",
        Name = name,
        CustomerId = "CUS-RUNNER-TEST",
        FromAccountType = AccountType.Savings,
        FromAccountId = "ACC-FROM-" + Guid.NewGuid().ToString("N")[..8],
        ToAccountType = AccountType.Savings,
        ToAccountId = "ACC-TO-" + Guid.NewGuid().ToString("N")[..8],
        TransferType = TransferType.AccountTransfer,
        InstructionType = InstructionType.Fixed,
        Priority = Priority.Medium,
        Amount = amount,
        Currency = "NGN",
        ValidFrom = new DateOnly(2026, 1, 1),
        ValidTill = null,
        RecurrenceType = RecurrenceType.Periodic,
        RecurrenceFrequency = RecurrenceFrequency.Days,
        RecurrenceInterval = 1,
        RecurrenceOnDay = null,
        AllowPartialTransfer = allowPartial,
        Status = StandingInstructionStatus.Active,
        LastRunDate = null,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        // A unique key/caller pair per instruction - these rows bypass CreateAsync entirely (direct
        // DbContext insertion), but the unique index on (InitiatedBy, IdempotencyKey) applies regardless;
        // leaving both null for every row would collide under AreNullsDistinct(false) once more than one
        // such instruction exists in the same test.
        IdempotencyKey = $"test-idem-{Guid.NewGuid():N}",
        InitiatedBy = "test-caller",
        RequestHash = "test-hash",
    };

    [Fact]
    public async Task RunDueInstructionsAsync_OnASuccessfulTransfer_RecordsASuccessRun_WithTheExactIdempotencyKeyFormat()
    {
        await using var dbContext = fixture.CreateContext();
        var instruction = ActiveDailyInstruction(nameof(RunDueInstructionsAsync_OnASuccessfulTransfer_RecordsASuccessRun_WithTheExactIdempotencyKeyFormat));
        dbContext.StandingInstructions.Add(instruction);
        await dbContext.SaveChangesAsync();

        var today = new DateOnly(2026, 3, 10);
        await CreateRunner(dbContext).RunDueInstructionsAsync(today, CancellationToken.None);

        var run = await dbContext.StandingInstructionRuns.AsNoTracking().SingleAsync(r => r.StandingInstructionId == instruction.Id);
        Assert.Equal(StandingInstructionRunStatus.Success, run.Status);
        Assert.Equal($"sched:{instruction.Id}:{today:yyyy-MM-dd}", run.IdempotencyKey);
        Assert.Equal(1, _orchestratorApiClient.CallCount);
        Assert.Equal(0, _ledgerApiClient.CallCount); // AllowPartialTransfer is false - never called.

        var reloaded = await dbContext.StandingInstructions.AsNoTracking().SingleAsync(s => s.Id == instruction.Id);
        Assert.Equal(today, reloaded.LastRunDate);
    }

    [Fact]
    public async Task RunDueInstructionsAsync_WhenOrchestratorApiThrows_RecordsAFailedRun_AndDoesNotRetrySameDayOnASecondTick()
    {
        await using var dbContext = fixture.CreateContext();
        var instruction = ActiveDailyInstruction(nameof(RunDueInstructionsAsync_WhenOrchestratorApiThrows_RecordsAFailedRun_AndDoesNotRetrySameDayOnASecondTick));
        dbContext.StandingInstructions.Add(instruction);
        await dbContext.SaveChangesAsync();

        _orchestratorApiClient.ThrowOnNextCall = FakeOrchestratorApiClient.Failure("insufficient funds");

        var today = new DateOnly(2026, 3, 10);
        var runner = CreateRunner(dbContext);

        await runner.RunDueInstructionsAsync(today, CancellationToken.None);
        Assert.Equal(1, _orchestratorApiClient.CallCount);

        var firstRun = await dbContext.StandingInstructionRuns.AsNoTracking().SingleAsync(r => r.StandingInstructionId == instruction.Id);
        Assert.Equal(StandingInstructionRunStatus.Failed, firstRun.Status);
        Assert.Contains("insufficient funds", firstRun.ErrorMessage);

        // A second tick the SAME day must not re-attempt (docs/DESIGN.md 3.3, D-33 decision 2: no
        // auto-retry before the instruction's own next naturally-scheduled occurrence).
        await runner.RunDueInstructionsAsync(today, CancellationToken.None);
        Assert.Equal(1, _orchestratorApiClient.CallCount); // still 1 - no second attempt.

        var runsAfterSecondTick = await dbContext.StandingInstructionRuns.AsNoTracking()
            .Where(r => r.StandingInstructionId == instruction.Id).ToListAsync();
        Assert.Single(runsAfterSecondTick); // still exactly one run row, still Failed.
        Assert.Equal(StandingInstructionRunStatus.Failed, runsAfterSecondTick[0].Status);

        // Only eligible again once LastRunDate rolls over to the instruction's own next real occurrence.
        var tomorrow = today.AddDays(1);
        await runner.RunDueInstructionsAsync(tomorrow, CancellationToken.None);
        Assert.Equal(2, _orchestratorApiClient.CallCount);
    }

    [Fact]
    public async Task RunDueInstructionsAsync_WhenAllowPartialTransferIsTrue_AndBalanceIsLessThanAmount_RecordsAPartialRun_ForTheLesserAmount()
    {
        await using var dbContext = fixture.CreateContext();
        var instruction = ActiveDailyInstruction(
            nameof(RunDueInstructionsAsync_WhenAllowPartialTransferIsTrue_AndBalanceIsLessThanAmount_RecordsAPartialRun_ForTheLesserAmount),
            amount: 1000m, allowPartial: true);
        dbContext.StandingInstructions.Add(instruction);
        await dbContext.SaveChangesAsync();

        _ledgerApiClient.Handler = account => new BalanceResponse(account, 400m, "NGN", DateTimeOffset.UtcNow);

        var today = new DateOnly(2026, 3, 10);
        await CreateRunner(dbContext).RunDueInstructionsAsync(today, CancellationToken.None);

        Assert.Equal(1, _ledgerApiClient.CallCount);
        Assert.Equal(instruction.FromAccountId, _ledgerApiClient.RequestedAccountNumbers.Single());
        Assert.Equal(1, _orchestratorApiClient.CallCount);
        Assert.Equal(400m, _orchestratorApiClient.Requests.Single().Amount);

        var run = await dbContext.StandingInstructionRuns.AsNoTracking().SingleAsync(r => r.StandingInstructionId == instruction.Id);
        Assert.Equal(StandingInstructionRunStatus.Partial, run.Status);
        Assert.Equal(400m, run.Amount);
    }

    [Fact]
    public async Task RunDueInstructionsAsync_WhenAllowPartialTransferIsTrue_AndBalanceIsZero_RecordsASkippedRun_AndSubmitsNothing()
    {
        await using var dbContext = fixture.CreateContext();
        var instruction = ActiveDailyInstruction(
            nameof(RunDueInstructionsAsync_WhenAllowPartialTransferIsTrue_AndBalanceIsZero_RecordsASkippedRun_AndSubmitsNothing),
            amount: 1000m, allowPartial: true);
        dbContext.StandingInstructions.Add(instruction);
        await dbContext.SaveChangesAsync();

        _ledgerApiClient.Handler = account => new BalanceResponse(account, 0m, "NGN", DateTimeOffset.UtcNow);

        var today = new DateOnly(2026, 3, 10);
        await CreateRunner(dbContext).RunDueInstructionsAsync(today, CancellationToken.None);

        Assert.Equal(1, _ledgerApiClient.CallCount);
        Assert.Equal(0, _orchestratorApiClient.CallCount); // nothing submitted.

        var run = await dbContext.StandingInstructionRuns.AsNoTracking().SingleAsync(r => r.StandingInstructionId == instruction.Id);
        Assert.Equal(StandingInstructionRunStatus.Skipped, run.Status);
        Assert.Null(run.OrchestratorReference);
    }

    [Fact]
    public async Task RunDueInstructionsAsync_WhenAllowPartialTransferIsFalse_NeverCallsLedgerApi_EvenWhenFundsWouldBeInsufficient()
    {
        await using var dbContext = fixture.CreateContext();
        var instruction = ActiveDailyInstruction(
            nameof(RunDueInstructionsAsync_WhenAllowPartialTransferIsFalse_NeverCallsLedgerApi_EvenWhenFundsWouldBeInsufficient),
            amount: 1000m, allowPartial: false);
        dbContext.StandingInstructions.Add(instruction);
        await dbContext.SaveChangesAsync();

        var today = new DateOnly(2026, 3, 10);
        await CreateRunner(dbContext).RunDueInstructionsAsync(today, CancellationToken.None);

        Assert.Equal(0, _ledgerApiClient.CallCount);
        Assert.Equal(1, _orchestratorApiClient.CallCount);
        Assert.Equal(1000m, _orchestratorApiClient.Requests.Single().Amount); // full amount submitted.
    }

    [Fact]
    public async Task RunDueInstructionsAsync_SkipsAnInstructionThatIsNotActive()
    {
        await using var dbContext = fixture.CreateContext();
        var instruction = ActiveDailyInstruction(nameof(RunDueInstructionsAsync_SkipsAnInstructionThatIsNotActive));
        instruction.Status = StandingInstructionStatus.Disabled;
        dbContext.StandingInstructions.Add(instruction);
        await dbContext.SaveChangesAsync();

        await CreateRunner(dbContext).RunDueInstructionsAsync(new DateOnly(2026, 3, 10), CancellationToken.None);

        Assert.Equal(0, _orchestratorApiClient.CallCount);
        Assert.False(await dbContext.StandingInstructionRuns.AnyAsync(r => r.StandingInstructionId == instruction.Id));
    }

    // End-to-end confirmation of the FINERACT-2914 clamping behavior (RecurrenceScheduleTests covers the
    // pure logic in isolation) - a day-31 Months instruction actually gets a run recorded on April 30,
    // through the real runner against a real database.
    [Fact]
    public async Task RunDueInstructionsAsync_MonthlyDay31Instruction_RunsOnApril30_InA30DayMonth()
    {
        await using var dbContext = fixture.CreateContext();
        var instruction = ActiveDailyInstruction(nameof(RunDueInstructionsAsync_MonthlyDay31Instruction_RunsOnApril30_InA30DayMonth));
        instruction.ValidFrom = new DateOnly(2026, 1, 31);
        instruction.RecurrenceFrequency = RecurrenceFrequency.Months;
        instruction.RecurrenceOnDay = 31;
        dbContext.StandingInstructions.Add(instruction);
        await dbContext.SaveChangesAsync();

        var runner = CreateRunner(dbContext);

        await runner.RunDueInstructionsAsync(new DateOnly(2026, 4, 29), CancellationToken.None);
        Assert.Equal(0, _orchestratorApiClient.CallCount);

        await runner.RunDueInstructionsAsync(new DateOnly(2026, 4, 30), CancellationToken.None);
        Assert.Equal(1, _orchestratorApiClient.CallCount);
    }
}
