using Garbati.ScheduleApi.Entities;
using Garbati.ScheduleApi.Scheduling;

namespace Garbati.ScheduleApi.Tests.Scheduling;

// docs/DESIGN.md Section 3.1/3.4 - pure due-date logic, including the FINERACT-2914 day-29-31 clamping
// lesson, tested directly with no database and no downstream clients.
public class RecurrenceScheduleTests
{
    private static StandingInstruction Make(
        DateOnly validFrom, DateOnly? validTill = null,
        RecurrenceFrequency frequency = RecurrenceFrequency.Months, int interval = 1, int? onDay = null) => new()
    {
        ValidFrom = validFrom,
        ValidTill = validTill,
        RecurrenceFrequency = frequency,
        RecurrenceInterval = interval,
        RecurrenceOnDay = onDay,
    };

    [Fact]
    public void IsDue_BeforeValidFrom_IsNeverDue()
    {
        var instruction = Make(new DateOnly(2026, 6, 1));

        Assert.False(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 5, 31)));
    }

    [Fact]
    public void IsDue_AfterValidTill_IsNeverDue()
    {
        var instruction = Make(new DateOnly(2026, 1, 1), validTill: new DateOnly(2026, 3, 1), frequency: RecurrenceFrequency.Days, interval: 1);

        Assert.False(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 3, 2)));
    }

    [Theory]
    [InlineData(1, "2026-01-01", true)]
    [InlineData(1, "2026-01-02", true)]
    [InlineData(3, "2026-01-04", true)]
    [InlineData(3, "2026-01-03", false)]
    public void IsDue_Daily_RespectsInterval(int interval, string todayIso, bool expected)
    {
        var instruction = Make(new DateOnly(2026, 1, 1), frequency: RecurrenceFrequency.Days, interval: interval);

        Assert.Equal(expected, RecurrenceSchedule.IsDue(instruction, DateOnly.Parse(todayIso)));
    }

    [Fact]
    public void IsDue_Weekly_OnlyOnTheConfiguredDayOfWeek()
    {
        // 2026-01-05 is a Monday; RecurrenceOnDay 3 = Wednesday (ISO, 1=Monday..7=Sunday).
        var instruction = Make(new DateOnly(2026, 1, 5), frequency: RecurrenceFrequency.Weeks, interval: 1, onDay: 3);

        Assert.False(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 1, 5))); // Monday
        Assert.True(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 1, 7))); // Wednesday
    }

    [Fact]
    public void IsDue_Weekly_RespectsIntervalInWeeks()
    {
        // Every 2 weeks, Monday.
        var instruction = Make(new DateOnly(2026, 1, 5), frequency: RecurrenceFrequency.Weeks, interval: 2, onDay: 1);

        Assert.True(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 1, 5))); // week 0
        Assert.False(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 1, 12))); // week 1 - skipped
        Assert.True(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 1, 19))); // week 2
    }

    // The FINERACT-2914 lesson: a day-31 Months recurrence due in a 30-day month runs on that month's
    // real last day - never throws, never silently skips.
    [Fact]
    public void IsDue_MonthlyDay31_ClampsToApril30_InA30DayMonth()
    {
        var instruction = Make(new DateOnly(2026, 1, 31), frequency: RecurrenceFrequency.Months, interval: 1, onDay: 31);

        Assert.False(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 4, 29)));
        Assert.True(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 4, 30)));
        // Never due twice for the same month, and April 31 does not exist to even ask about.
    }

    [Fact]
    public void IsDue_MonthlyDay31_ClampsToFebruary28_InANonLeapYear()
    {
        // 2026 is not a leap year.
        var instruction = Make(new DateOnly(2026, 1, 31), frequency: RecurrenceFrequency.Months, interval: 1, onDay: 31);

        Assert.True(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 2, 28)));
        Assert.False(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 2, 27)));
    }

    [Fact]
    public void IsDue_MonthlyDay31_ClampsToFebruary29_InALeapYear()
    {
        // 2028 is a leap year.
        var instruction = Make(new DateOnly(2026, 1, 31), frequency: RecurrenceFrequency.Months, interval: 1, onDay: 31);

        Assert.True(RecurrenceSchedule.IsDue(instruction, new DateOnly(2028, 2, 29)));
        Assert.False(RecurrenceSchedule.IsDue(instruction, new DateOnly(2028, 2, 28)));
    }

    [Fact]
    public void IsDue_MonthlyDay31_RunsOnDay31_InA31DayMonth()
    {
        var instruction = Make(new DateOnly(2026, 1, 31), frequency: RecurrenceFrequency.Months, interval: 1, onDay: 31);

        Assert.True(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 3, 31)));
    }

    [Fact]
    public void IsDue_Monthly_RespectsIntervalInMonths()
    {
        var instruction = Make(new DateOnly(2026, 1, 15), frequency: RecurrenceFrequency.Months, interval: 3, onDay: 15);

        Assert.True(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 1, 15)));
        Assert.False(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 2, 15)));
        Assert.False(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 3, 15)));
        Assert.True(RecurrenceSchedule.IsDue(instruction, new DateOnly(2026, 4, 15)));
    }

    // Yearly, anchored to ValidFrom's own month (no RecurrenceOnMonth field - see RecurrenceSchedule's
    // own top comment and the PR description for the flagged task-file/docs/DESIGN.md mismatch).
    [Fact]
    public void IsDue_Yearly_ClampsDay29_ToFebruary28_InANonLeapYear()
    {
        var instruction = Make(new DateOnly(2024, 2, 29), frequency: RecurrenceFrequency.Years, interval: 1, onDay: 29);

        Assert.True(RecurrenceSchedule.IsDue(instruction, new DateOnly(2025, 2, 28)));
        Assert.True(RecurrenceSchedule.IsDue(instruction, new DateOnly(2028, 2, 29)));
    }

    [Fact]
    public void IsDue_Yearly_OnlyInValidFromsMonth()
    {
        var instruction = Make(new DateOnly(2026, 3, 15), frequency: RecurrenceFrequency.Years, interval: 1, onDay: 15);

        Assert.False(RecurrenceSchedule.IsDue(instruction, new DateOnly(2027, 4, 15)));
        Assert.True(RecurrenceSchedule.IsDue(instruction, new DateOnly(2027, 3, 15)));
    }
}
