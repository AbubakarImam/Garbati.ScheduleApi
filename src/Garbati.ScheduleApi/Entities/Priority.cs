namespace Garbati.ScheduleApi.Entities;

// DESIGN.md Section 2.1/5 (T-069 Finding 6). Modeled for parity; deliberately NOT enforced for
// fund-contention ordering in v1 - T-069's own research could not confirm Fineract's own shipped code
// enforces it either. Runs are processed in a simple, deterministic order (ascending StandingInstruction
// internal Id) instead of inventing an ordering guarantee neither this design nor Fineract's confirmed
// behavior backs up.
public enum Priority
{
    Urgent,
    High,
    Medium,
    Low,
}
