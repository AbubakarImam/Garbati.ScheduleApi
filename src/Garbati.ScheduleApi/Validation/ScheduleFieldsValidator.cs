using Garbati.ScheduleApi.Entities;

namespace Garbati.ScheduleApi.Validation;

// Shape rules shared by Create/UpdateStandingInstructionRequestValidator for the fields both requests
// carry (docs/DESIGN.md Section 2.1's ValidFrom/ValidTill/RecurrenceType/RecurrenceFrequency/
// RecurrenceInterval/RecurrenceOnDay/Amount group) - kept in one place so the two validators cannot drift
// apart on what "a sane schedule" means.
public static class ScheduleFieldsValidator
{
    public static void Validate(
        decimal amount, DateOnly validFrom, DateOnly? validTill,
        string? recurrenceType, string? recurrenceFrequency, int recurrenceInterval, int? recurrenceOnDay,
        List<string> errors)
    {
        if (amount <= 0m)
        {
            errors.Add("Amount must be greater than zero.");
        }

        if (validFrom == default)
        {
            errors.Add("ValidFrom is required.");
        }

        if (validTill is { } till && till < validFrom)
        {
            errors.Add("ValidTill must not be before ValidFrom.");
        }

        var parsedRecurrenceType = EnumParser.Parse<RecurrenceType>(recurrenceType);
        if (parsedRecurrenceType is null)
        {
            errors.Add("RecurrenceType must be Periodic or AsPerDues.");
        }

        var parsedFrequency = EnumParser.Parse<RecurrenceFrequency>(recurrenceFrequency);
        if (parsedFrequency is null)
        {
            errors.Add("RecurrenceFrequency must be Days, Weeks, Months or Years.");
        }

        if (recurrenceInterval < 1)
        {
            errors.Add("RecurrenceInterval must be at least 1.");
        }

        // DESIGN.md 2.1: "day of month (Months/Years) or day of week (Weeks)" - unused for Days, so only
        // range-checked when the frequency actually needs it.
        if (recurrenceOnDay is { } day && parsedFrequency is { } frequency)
        {
            switch (frequency)
            {
                case RecurrenceFrequency.Weeks when day is < 1 or > 7:
                    errors.Add("RecurrenceOnDay must be between 1 and 7 (day of week) when RecurrenceFrequency is Weeks.");
                    break;
                case RecurrenceFrequency.Months or RecurrenceFrequency.Years when day is < 1 or > 31:
                    errors.Add("RecurrenceOnDay must be between 1 and 31 (day of month) when RecurrenceFrequency is Months or Years.");
                    break;
            }
        }
    }
}
