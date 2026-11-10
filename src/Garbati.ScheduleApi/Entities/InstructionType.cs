namespace Garbati.ScheduleApi.Entities;

// DESIGN.md Section 2.1. v1: only Fixed is accepted at creation - Dues ("whatever is currently owed")
// requires a linked loan's own repayment schedule, rejected clearly (NoFinancingModuleException) until
// GARBATI has a financing module.
public enum InstructionType
{
    Fixed,
    Dues,
}
