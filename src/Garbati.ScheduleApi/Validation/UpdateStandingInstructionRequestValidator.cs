using Garbati.ScheduleApi.Contracts.Requests;
using Garbati.ScheduleApi.Entities;

namespace Garbati.ScheduleApi.Validation;

public class UpdateStandingInstructionRequestValidator
{
    public IReadOnlyList<string> Validate(UpdateStandingInstructionRequest request)
    {
        var errors = new List<string>();

        if (EnumParser.Parse<Priority>(request.Priority) is null)
        {
            errors.Add("Priority must be Urgent, High, Medium or Low.");
        }

        ScheduleFieldsValidator.Validate(
            request.Amount, request.ValidFrom, request.ValidTill,
            request.RecurrenceType, request.RecurrenceFrequency, request.RecurrenceInterval, request.RecurrenceOnDay,
            errors);

        return errors;
    }
}
