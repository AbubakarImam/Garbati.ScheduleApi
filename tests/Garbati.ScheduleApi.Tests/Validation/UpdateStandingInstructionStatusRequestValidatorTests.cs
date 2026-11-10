using Garbati.ScheduleApi.Contracts.Requests;
using Garbati.ScheduleApi.Validation;

namespace Garbati.ScheduleApi.Tests.Validation;

public class UpdateStandingInstructionStatusRequestValidatorTests
{
    private readonly UpdateStandingInstructionStatusRequestValidator _validator = new();

    [Theory]
    [InlineData("Active")]
    [InlineData("active")]
    [InlineData("Disabled")]
    [InlineData("disabled")]
    public void Validate_AcceptsActiveOrDisabled_CaseInsensitively(string status)
    {
        var errors = _validator.Validate(new UpdateStandingInstructionStatusRequest(status));

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RejectsDeleted_EvenThoughItIsAValidEnumValue()
    {
        var errors = _validator.Validate(new UpdateStandingInstructionStatusRequest("Deleted"));

        Assert.Contains(errors, e => e.Contains("Status"));
    }

    [Fact]
    public void Validate_RejectsAnUnrecognizedValue()
    {
        var errors = _validator.Validate(new UpdateStandingInstructionStatusRequest("Paused"));

        Assert.Contains(errors, e => e.Contains("Status"));
    }
}
