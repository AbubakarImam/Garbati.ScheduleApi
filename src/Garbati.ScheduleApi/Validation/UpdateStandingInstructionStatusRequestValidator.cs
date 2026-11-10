using Garbati.ScheduleApi.Contracts.Requests;
using Garbati.ScheduleApi.Entities;

namespace Garbati.ScheduleApi.Validation;

// docs/DESIGN.md Section 4: PATCH .../status is "Active <-> Disabled only" - Deleted is a valid
// StandingInstructionStatus value but never a valid REQUEST to this endpoint, so it is rejected here
// (400) exactly like any other unrecognized string, not treated as "parses fine, business rule says no."
public class UpdateStandingInstructionStatusRequestValidator
{
    public IReadOnlyList<string> Validate(UpdateStandingInstructionStatusRequest request)
    {
        var errors = new List<string>();

        var parsed = EnumParser.Parse<StandingInstructionStatus>(request.Status);
        if (parsed is not (StandingInstructionStatus.Active or StandingInstructionStatus.Disabled))
        {
            errors.Add("Status must be Active or Disabled.");
        }

        return errors;
    }
}
