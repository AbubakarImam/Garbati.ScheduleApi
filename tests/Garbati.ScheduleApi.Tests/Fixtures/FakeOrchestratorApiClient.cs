using Garbati.ScheduleApi.Clients.Contracts.Orchestrator;
using Garbati.ScheduleApi.Clients.Interfaces;
using Garbati.ScheduleApi.Exceptions;

namespace Garbati.ScheduleApi.Tests.Fixtures;

// Hand-rolled fake (no mocking library, matching Garbati.OrchestratorApi's own test project convention).
// Default behavior is a plain Success, same shape FakeLedgerApiClient.TransferAsync's own default uses in
// that repo's test suite.
public class FakeOrchestratorApiClient : IOrchestratorApiClient
{
    public int CallCount { get; private set; }
    public List<TransferRequest> Requests { get; } = [];

    public Func<TransferRequest, TransferResultResponse>? Handler { get; set; }
    public Exception? ThrowOnNextCall { get; set; }

    public Task<TransferResultResponse> SubmitTransferAsync(TransferRequest request, CancellationToken cancellationToken)
    {
        CallCount++;
        Requests.Add(request);

        if (ThrowOnNextCall is { } exception)
        {
            ThrowOnNextCall = null;
            throw exception;
        }

        var handler = Handler ?? (r => new TransferResultResponse($"DEC-FAKE{CallCount:D8}", $"TXN-FAKE{CallCount:D8}", "Success", null));
        return Task.FromResult(handler(request));
    }

    public static DownstreamApiException Failure(string message) =>
        new("OrchestratorApi", 500, message);
}
