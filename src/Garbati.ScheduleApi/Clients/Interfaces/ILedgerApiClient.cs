using Garbati.ScheduleApi.Clients.Contracts.Ledger;

namespace Garbati.ScheduleApi.Clients.Interfaces;

// ScheduleApi as a client of LedgerApi's existing, real GET /api/accounts/{accountNumber}/balance -
// the narrow, read-only D-33 exception (docs/DESIGN.md 3.2). Nothing else ScheduleApi does touches
// LedgerApi directly.
public interface ILedgerApiClient
{
    // Null when the account does not exist (LedgerApi's own GetBalance returns 404 in that case) -
    // StandingInstructionRunner treats this the same way as a non-positive balance (DESIGN.md 3.2:
    // "or skip entirely if the balance is zero or negative").
    Task<BalanceResponse?> GetBalanceAsync(string accountNumber, CancellationToken cancellationToken);
}
