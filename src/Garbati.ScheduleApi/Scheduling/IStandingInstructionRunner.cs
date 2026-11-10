namespace Garbati.ScheduleApi.Scheduling;

// Separated from the BackgroundService itself so tests can call RunDueInstructionsAsync directly with a
// controlled "today" - no real waiting for an hourly tick, no manipulating the system clock.
public interface IStandingInstructionRunner
{
    Task RunDueInstructionsAsync(DateOnly today, CancellationToken cancellationToken);
}
