namespace Garbati.ScheduleApi.Entities;

// DESIGN.md Section 2.2 (decision #6): GARBATI's own closed set, not Fineract's unconfirmed literal
// strings. Skipped (a GARBATI addition, not Fineract-sourced): a run that was due but never submitted at
// all (e.g. AllowPartialTransfer's balance check found nothing affordable) - distinct from Failed (a run
// that WAS submitted and came back rejected), so run history can honestly answer "did we even try."
public enum StandingInstructionRunStatus
{
    Success,
    Partial,
    Failed,
    Skipped,
}
