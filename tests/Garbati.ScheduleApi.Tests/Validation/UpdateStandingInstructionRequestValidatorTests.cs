using Garbati.ScheduleApi.Contracts.Requests;
using Garbati.ScheduleApi.Validation;

namespace Garbati.ScheduleApi.Tests.Validation;

public class UpdateStandingInstructionRequestValidatorTests
{
    private readonly UpdateStandingInstructionRequestValidator _validator = new();

    private static UpdateStandingInstructionRequest Valid() => new(
        100m, new DateOnly(2026, 1, 1), null, "Periodic", "Months", 1, 1, "Medium", false);

    [Fact]
    public void Validate_WithAValidRequest_ReturnsNoErrors()
    {
        Assert.Empty(_validator.Validate(Valid()));
    }

    [Fact]
    public void Validate_WhenPriorityIsNotARecognizedValue_ReturnsAnError()
    {
        var request = Valid() with { Priority = "Urgentish" };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("Priority"));
    }

    [Fact]
    public void Validate_WhenAmountIsZeroOrNegative_ReturnsAnError()
    {
        var request = Valid() with { Amount = -1m };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("Amount"));
    }

    [Fact]
    public void Validate_WhenRecurrenceTypeIsNotARecognizedValue_ReturnsAnError()
    {
        var request = Valid() with { RecurrenceType = "NotARealType" };

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Contains("RecurrenceType"));
    }
}
