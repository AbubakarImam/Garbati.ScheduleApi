namespace Garbati.ScheduleApi.Contracts.Requests;

// docs/DESIGN.md Section 4's PUT row: "amount, schedule, priority, AllowPartialTransfer" - deliberately
// narrower than CreateStandingInstructionRequest. Name/CustomerId/the four account fields/TransferType/
// InstructionType are NOT editable through this endpoint (not listed in Section 4, and Fineract's own
// confirmed update() behavior T-069 Finding 7 cites does not reopen those either) - changing the parties
// or kind of a standing instruction is a cancel-and-recreate workflow, this design's real in-place edit is
// for the scheduling/amount/priority knobs only. RecurrenceType is still restricted to Periodic
// (re-validated here, not just at creation) so PUT cannot be used to smuggle an instruction into
// AsPerDues after the fact.
public record UpdateStandingInstructionRequest(
    decimal Amount,
    DateOnly ValidFrom,
    DateOnly? ValidTill,
    string RecurrenceType,
    string RecurrenceFrequency,
    int RecurrenceInterval,
    int? RecurrenceOnDay,
    string Priority,
    bool AllowPartialTransfer);
