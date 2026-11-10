namespace Garbati.ScheduleApi.Exceptions;

// docs/DESIGN.md Section 4: PUT is rejected on a Deleted instruction, matching Fineract's own confirmed
// rule exactly (T-069 Finding 7). Also used by PATCH .../status: a Deleted instruction cannot be
// reactivated through the Active/Disabled toggle - DELETE is a one-way door in v1.
public class StandingInstructionDeletedException(string standingInstructionId)
    : Exception($"Standing instruction '{standingInstructionId}' has been deleted and cannot be changed.");
