using System.Net;
using System.Text;

namespace Garbati.ScheduleApi.Tests.Clients;

// A hand-rolled fake HttpMessageHandler (no mocking library, matching Garbati.OrchestratorApi's own test
// project convention) for testing OrchestratorApiClient/LedgerApiClient's own request-building and
// response-parsing logic in isolation, without a real network call.
public class FakeHttpMessageHandler(HttpStatusCode statusCode, string? jsonBody, string contentType = "application/json")
    : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        var response = new HttpResponseMessage(statusCode);
        if (jsonBody is not null)
        {
            response.Content = new StringContent(jsonBody, Encoding.UTF8, contentType);
        }
        return response;
    }
}
