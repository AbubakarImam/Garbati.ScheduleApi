using Garbati.ScheduleApi.Clients.Contracts.Ledger;
using Garbati.ScheduleApi.Clients.Interfaces;

namespace Garbati.ScheduleApi.Tests.Fixtures;

// Hand-rolled fake, same approach as FakeOrchestratorApiClient. Default: no configured balance (returns
// null, the "account not found" shape) - tests that care about a real balance set Handler explicitly, so
// a test that forgets to configure this fails loudly (a null-propagated Skipped run) rather than silently
// behaving like plenty of money is available.
public class FakeLedgerApiClient : ILedgerApiClient
{
    public int CallCount { get; private set; }
    public List<string> RequestedAccountNumbers { get; } = [];

    public Func<string, BalanceResponse?>? Handler { get; set; }

    public Task<BalanceResponse?> GetBalanceAsync(string accountNumber, CancellationToken cancellationToken)
    {
        CallCount++;
        RequestedAccountNumbers.Add(accountNumber);

        var handler = Handler ?? (_ => null);
        return Task.FromResult(handler(accountNumber));
    }
}
