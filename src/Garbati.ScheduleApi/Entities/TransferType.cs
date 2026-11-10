namespace Garbati.ScheduleApi.Entities;

// DESIGN.md Section 2.1 (T-069 Finding 1, Fineract's own real, confirmed enum). v1: only AccountTransfer
// is accepted at creation - the other four all require a loan on one side; modeled now for schema parity,
// rejected clearly (NoFinancingModuleException) until GARBATI has a financing module.
public enum TransferType
{
    AccountTransfer,
    LoanRepayment,
    ChargePayment,
    InterestTransfer,
    LoanDownPayment,
}
