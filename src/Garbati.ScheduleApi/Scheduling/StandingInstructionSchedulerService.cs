namespace Garbati.ScheduleApi.Scheduling;

// docs/DESIGN.md Section 3.1: a plain ASP.NET Core BackgroundService, polling hourly - no new NuGet
// dependency, no Quartz, no Hangfire. A new scope per tick: IStandingInstructionRunner and its
// ScheduleDbContext are scoped services, and a BackgroundService itself is a singleton, so it cannot
// depend on them directly (same reason every ASP.NET Core BackgroundService that touches a DbContext
// needs IServiceScopeFactory - this is framework plumbing, not a design choice specific to this task).
public class StandingInstructionSchedulerService(
    IServiceScopeFactory scopeFactory,
    ILogger<StandingInstructionSchedulerService> logger) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Runs once immediately on startup (so a short-lived demo/dev run still sees a tick), then every
        // PollInterval thereafter. docs/DESIGN.md 7's own open question #1: the hourly interval is not
        // load-tested, chosen as "enough to catch every due date reliably without needing second-level
        // precision."
        using var timer = new PeriodicTimer(PollInterval);

        do
        {
            await TickAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IStandingInstructionRunner>();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            await runner.RunDueInstructionsAsync(today, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal on shutdown - the loop's own while condition stops it from ticking again.
        }
        catch (Exception ex)
        {
            // One bad tick must never take the whole BackgroundService down - the next tick (an hour from
            // now) tries again. Individual instruction failures are already isolated inside
            // StandingInstructionRunner; this is the outer safety net for anything that escapes that
            // (e.g. the database itself being briefly unreachable).
            logger.LogError(ex, "Standing instruction scheduler tick failed");
        }
    }
}
