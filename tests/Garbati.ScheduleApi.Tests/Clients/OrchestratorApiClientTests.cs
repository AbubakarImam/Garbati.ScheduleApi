using System.Net;
using Garbati.ScheduleApi.Clients.Contracts.Orchestrator;
using Garbati.ScheduleApi.Clients.Implementations;
using Garbati.ScheduleApi.Exceptions;

namespace Garbati.ScheduleApi.Tests.Clients;

public class OrchestratorApiClientTests
{
    private static OrchestratorApiClient CreateClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://orchestrator.invalid") });

    [Fact]
    public async Task SubmitTransferAsync_On200_ReturnsTheResponse()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
            """{"decisionId":"DEC-1","reference":"TXN-1","status":"Success","failureReason":null}""");
        var client = CreateClient(handler);

        var result = await client.SubmitTransferAsync(
            new TransferRequest("sched:1:2026-01-01", "CUS-1", "ACC-FROM", "ACC-TO", 100m, "NGN", null),
            CancellationToken.None);

        Assert.Equal("Success", result.Status);
        Assert.Equal("TXN-1", result.Reference);
    }

    // OrchestratorApi's own TransfersController returns a real TransferResultResponse body on 422 when
    // LedgerApi declined the leg (Status="Failed") - that is data, not an exception.
    [Fact]
    public async Task SubmitTransferAsync_On422_WithARealTransferResultBody_ReturnsItAsData_DoesNotThrow()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.UnprocessableEntity,
            """{"decisionId":"DEC-1","reference":"TXN-1","status":"Failed","failureReason":"Insufficient funds"}""",
            "application/json");
        var client = CreateClient(handler);

        var result = await client.SubmitTransferAsync(
            new TransferRequest("sched:1:2026-01-01", "CUS-1", "ACC-FROM", "ACC-TO", 100m, "NGN", null),
            CancellationToken.None);

        Assert.Equal("Failed", result.Status);
        Assert.Equal("Insufficient funds", result.FailureReason);
    }

    // The case this task was explicitly told to get right: a 422 that is EXCEPTION-mapped
    // (AuthorizationDeniedException, application/problem+json, no Reference/Status fields at all) must
    // throw, not silently deserialize into a TransferResultResponse with every field null.
    [Fact]
    public async Task SubmitTransferAsync_On422_WithAProblemDetailsBody_Throws_DoesNotReturnANullFilledResponse()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.UnprocessableEntity,
            """{"title":"Authorization denied.","detail":"ApprovalPolicyNotSingleHolder","decisionId":"DEC-1","reasonCode":"ApprovalPolicyNotSingleHolder","correlationId":"abc"}""",
            "application/problem+json");
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<DownstreamApiException>(() => client.SubmitTransferAsync(
            new TransferRequest("sched:1:2026-01-01", "CUS-1", "ACC-FROM", "ACC-TO", 100m, "NGN", null),
            CancellationToken.None));

        Assert.Contains("ApprovalPolicyNotSingleHolder", ex.Message);
    }

    [Fact]
    public async Task SubmitTransferAsync_On500_Throws()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError,
            """{"title":"An unexpected error occurred."}""", "application/problem+json");
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<DownstreamApiException>(() => client.SubmitTransferAsync(
            new TransferRequest("sched:1:2026-01-01", "CUS-1", "ACC-FROM", "ACC-TO", 100m, "NGN", null),
            CancellationToken.None));
    }

    [Fact]
    public async Task SubmitTransferAsync_SendsTheRequestToTheExpectedPath()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK,
            """{"decisionId":"DEC-1","reference":"TXN-1","status":"Success","failureReason":null}""");
        var client = CreateClient(handler);

        await client.SubmitTransferAsync(
            new TransferRequest("sched:1:2026-01-01", "CUS-1", "ACC-FROM", "ACC-TO", 100m, "NGN", null),
            CancellationToken.None);

        Assert.Equal("v1/transfers", handler.LastRequest!.RequestUri!.AbsolutePath.TrimStart('/'));
    }
}
