namespace Garbati.ScheduleApi.Entities;

// DESIGN.md Section 2.1. Fineract's own real axis (confirmed independently nullable on each side of its
// m_account_transfer_details table, T-069) - modeled on both FromAccountType and ToAccountType for schema
// parity. v1: only Savings is accepted at creation (StandingInstructionService.ValidateFinancingScope) -
// Loan is rejected clearly with a NoFinancingModuleException until GARBATI has a financing module.
public enum AccountType
{
    Savings,
    Loan,
}
