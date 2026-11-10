namespace Garbati.ScheduleApi.Entities;

// DESIGN.md Section 2.1. v1: only Periodic is accepted at creation (and on PUT - the update validator
// re-applies the same rule so a caller cannot create a Periodic instruction and later edit it into
// AsPerDues). AsPerDues (fire whenever something is owed on a linked loan) requires a financing module;
// rejected clearly (NoFinancingModuleException) until one exists.
public enum RecurrenceType
{
    Periodic,
    AsPerDues,
}
