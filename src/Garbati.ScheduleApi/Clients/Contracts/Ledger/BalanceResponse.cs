namespace Garbati.ScheduleApi.Clients.Contracts.Ledger;

// Mirrors LedgerApi.Contracts.Responses.BalanceResponse exactly (read directly from that repo's own
// source - task file instruction). The one, narrow field ScheduleApi ever reads directly from LedgerApi
// (docs/DESIGN.md 3.2, D-33 decision 4).
public record BalanceResponse(
    string AccountNumber,
    decimal Balance,
    string CurrencyCode,
    DateTimeOffset AsOf);
