using Garbati.ScheduleApi.Entities;

namespace Garbati.ScheduleApi.Scheduling;

// docs/DESIGN.md Section 3.1/3.4. Pure, deterministic "is this instruction due on this calendar date"
// logic - kept separate from StandingInstructionRunner so the FINERACT-2914 clamping behavior and the
// Days/Weeks/Months/Years recurrence math can be unit-tested directly, with no database and no fake
// downstream clients involved.
//
// RecurrenceOnMonth deviation (flagged in the PR description and docs/DESIGN.md): the task file mentions
// "RecurrenceOnDay/RecurrenceOnMonth" when describing the due-date check, but docs/DESIGN.md Section
// 2.1's own StandingInstruction field table lists only RecurrenceOnDay - no RecurrenceOnMonth field
// exists anywhere in the design. Reading the table literally (as instructed): a Years-frequency
// instruction's month is therefore anchored to ValidFrom's own month, not a separately configurable
// month. This is the one place this task found the task file's own summary describing a field the
// design doc it points to does not actually define.
public static class RecurrenceSchedule
{
    public static bool IsDue(StandingInstruction instruction, DateOnly today)
    {
        if (today < instruction.ValidFrom)
        {
            return false;
        }

        if (instruction.ValidTill is { } validTill && today > validTill)
        {
            return false;
        }

        var interval = Math.Max(instruction.RecurrenceInterval, 1);

        return instruction.RecurrenceFrequency switch
        {
            RecurrenceFrequency.Days => IsDueDaily(instruction.ValidFrom, today, interval),
            RecurrenceFrequency.Weeks => IsDueWeekly(instruction, today, interval),
            RecurrenceFrequency.Months => IsDueMonthly(instruction, today, interval, yearly: false),
            RecurrenceFrequency.Years => IsDueMonthly(instruction, today, interval, yearly: true),
            _ => false,
        };
    }

    private static bool IsDueDaily(DateOnly validFrom, DateOnly today, int intervalDays)
    {
        var daysSince = today.DayNumber - validFrom.DayNumber;
        return daysSince >= 0 && daysSince % intervalDays == 0;
    }

    private static bool IsDueWeekly(StandingInstruction instruction, DateOnly today, int intervalWeeks)
    {
        // RecurrenceOnDay (1 = Monday ... 7 = Sunday, ISO 8601) when given; otherwise ValidFrom's own
        // day of week, so an instruction created without one still has a well-defined weekly anchor.
        var targetDayOfWeek = instruction.RecurrenceOnDay is >= 1 and <= 7
            ? IsoDayOfWeek(instruction.RecurrenceOnDay.Value)
            : instruction.ValidFrom.DayOfWeek;

        if (today.DayOfWeek != targetDayOfWeek)
        {
            return false;
        }

        var anchorWeekStart = StartOfIsoWeek(instruction.ValidFrom);
        var todayWeekStart = StartOfIsoWeek(today);
        var weeksSince = (todayWeekStart.DayNumber - anchorWeekStart.DayNumber) / 7;
        return weeksSince >= 0 && weeksSince % intervalWeeks == 0;
    }

    // The FINERACT-2914 lesson (docs/DESIGN.md 3.4): RecurrenceOnDay is clamped to the target month's
    // real length, never truncated and never allowed to throw. A day-31 instruction due in a 30-day
    // month runs on day 30 of that month - every month, independently, not "skip this month" and not
    // "crash."
    private static bool IsDueMonthly(StandingInstruction instruction, DateOnly today, int interval, bool yearly)
    {
        var anchorMonth = instruction.ValidFrom.Month;
        var anchorYear = instruction.ValidFrom.Year;
        var targetDay = instruction.RecurrenceOnDay ?? instruction.ValidFrom.Day;

        if (yearly)
        {
            // No RecurrenceOnMonth field (see this file's own top comment) - anchored to ValidFrom's month.
            if (today.Month != anchorMonth)
            {
                return false;
            }

            var yearsSince = today.Year - anchorYear;
            if (yearsSince < 0 || yearsSince % interval != 0)
            {
                return false;
            }
        }
        else
        {
            var monthsSince = ((today.Year - anchorYear) * 12) + (today.Month - anchorMonth);
            if (monthsSince < 0 || monthsSince % interval != 0)
            {
                return false;
            }
        }

        var daysInTargetMonth = DateTime.DaysInMonth(today.Year, today.Month);
        var clampedDay = Math.Clamp(targetDay, 1, daysInTargetMonth);
        return today.Day == clampedDay;
    }

    private static DayOfWeek IsoDayOfWeek(int isoDay) => isoDay == 7 ? DayOfWeek.Sunday : (DayOfWeek)isoDay;

    private static DateOnly StartOfIsoWeek(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }
}
