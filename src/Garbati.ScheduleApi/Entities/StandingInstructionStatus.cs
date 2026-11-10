namespace Garbati.ScheduleApi.Entities;

// DESIGN.md Section 2.1 (T-069 Finding 4, Fineract's own real three-value enum). Soft lifecycle: DELETE
// sets Deleted rather than removing the row; PATCH .../status only ever toggles Active <-> Disabled.
public enum StandingInstructionStatus
{
    Active,
    Disabled,
    Deleted,
}
