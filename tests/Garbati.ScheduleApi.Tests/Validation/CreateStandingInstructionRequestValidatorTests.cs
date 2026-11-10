using Garbati.ScheduleApi.Contracts.Requests;
using Garbati.ScheduleApi.Validation;

namespace Garbati.ScheduleApi.Tests.Validation;

public class CreateStandingInstructionRequestValidatorTests
{
    private readonly CreateStandingInstructionRequestValidator _validator = new();

    private static CreateStandingInstructionRequest Valid() => new(
        "a-valid-name", "CUS-1", "Savings", "ACC-FROM", "Savings", "ACC-TO",
        "AccountTransfer", "Fixed", "Medium", 100m, "NGN", null,
        new DateOnly(2026, 1, 1), null, "Periodic", "Months", 1, 1, false);

    [Fact]
    public void Validate_WithAValidRequest_ReturnsNoErrors()
    {
        var errors = _validator.Validate(Valid());

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("savings")]
    [InlineData("Savings")]
    [InlineData("loan")]
    [InlineData("Loan")]
    public void Validate_AcceptsAccountType_CaseInsensitively(string accountType)
    {
        var request = Valid() with { FromAccountType = accountType, ToAccountType = accountType };

        var errors = _validator.Validate(request);

        Assert.DoesNotContain(errors, e => e.Contains("FromAccountType") || e.Contains("ToAccountType"));
    }

    [Fact]
    public void Validate_WhenFromAccountTypeIsNotARecognizedValue_ReturnsAnError()
    {
        var request = Valid() with { FromAccountType = "Current" };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("FromAccountType"));
    }

    [Fact]
    public void Validate_WhenToAccountTypeIsNotARecognizedValue_ReturnsAnError()
    {
        var request = Valid() with { ToAccountType = "Current" };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("ToAccountType"));
    }

    [Fact]
    public void Validate_WhenFromAndToAccountIdsAreTheSame_ReturnsAnError()
    {
        var request = Valid() with { ToAccountId = Valid().FromAccountId };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("must be different"));
    }

    [Fact]
    public void Validate_WhenTransferTypeIsNotARecognizedValue_ReturnsAnError()
    {
        var request = Valid() with { TransferType = "NotARealType" };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("TransferType"));
    }

    [Fact]
    public void Validate_WhenInstructionTypeIsNotARecognizedValue_ReturnsAnError()
    {
        var request = Valid() with { InstructionType = "NotARealType" };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("InstructionType"));
    }

    [Fact]
    public void Validate_WhenPriorityIsNotARecognizedValue_ReturnsAnError()
    {
        var request = Valid() with { Priority = "NotARealValue" };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("Priority"));
    }

    [Fact]
    public void Validate_WhenRecurrenceTypeIsNotARecognizedValue_ReturnsAnError()
    {
        var request = Valid() with { RecurrenceType = "NotARealType" };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("RecurrenceType"));
    }

    [Fact]
    public void Validate_WhenRecurrenceFrequencyIsNotARecognizedValue_ReturnsAnError()
    {
        var request = Valid() with { RecurrenceFrequency = "Fortnightly" };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("RecurrenceFrequency"));
    }

    [Fact]
    public void Validate_WhenAmountIsZeroOrNegative_ReturnsAnError()
    {
        var request = Valid() with { Amount = 0m };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("Amount"));
    }

    [Fact]
    public void Validate_WhenRecurrenceIntervalIsLessThanOne_ReturnsAnError()
    {
        var request = Valid() with { RecurrenceInterval = 0 };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("RecurrenceInterval"));
    }

    [Fact]
    public void Validate_WhenValidTillIsBeforeValidFrom_ReturnsAnError()
    {
        var request = Valid() with { ValidTill = new DateOnly(2025, 1, 1) };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("ValidTill"));
    }

    [Fact]
    public void Validate_WhenRecurrenceOnDayIsOutOfRangeForMonths_ReturnsAnError()
    {
        var request = Valid() with { RecurrenceFrequency = "Months", RecurrenceOnDay = 32 };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("RecurrenceOnDay"));
    }

    [Fact]
    public void Validate_WhenRecurrenceOnDayIsOutOfRangeForWeeks_ReturnsAnError()
    {
        var request = Valid() with { RecurrenceFrequency = "Weeks", RecurrenceOnDay = 8 };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("RecurrenceOnDay"));
    }

    [Fact]
    public void Validate_WhenCurrencyIsNotThreeLetters_ReturnsAnError()
    {
        var request = Valid() with { Currency = "N" };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("Currency"));
    }

    [Fact]
    public void Validate_WhenNameIsMissing_ReturnsAnError()
    {
        var request = Valid() with { Name = "" };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("Name"));
    }

    // Note: Loan account type / non-AccountTransfer / non-Fixed / non-Periodic are deliberately NOT
    // tested here - this validator accepts every recognized enum value (shape-only). The create-time
    // rejection of those specific not-yet-supported combinations is a 422 business rule
    // (NoFinancingModuleException from StandingInstructionService), covered by
    // Controllers/CreateStandingInstructionTests instead.
}
