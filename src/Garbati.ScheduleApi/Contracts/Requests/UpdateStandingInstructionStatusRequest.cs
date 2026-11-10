namespace Garbati.ScheduleApi.Contracts.Requests;

// docs/DESIGN.md Section 4's PATCH .../status row: "Active <-> Disabled only." Status is a plain string
// (Validation/EnumParser) like every other enum-shaped request field; the validator rejects anything
// other than Active/Disabled, including the literal string "Deleted" - a caller cannot use this endpoint
// to undo a DELETE or to delete through it.
public record UpdateStandingInstructionStatusRequest(string Status);
