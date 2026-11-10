using Garbati.ScheduleApi.Middleware;

namespace Garbati.ScheduleApi.Clients.Implementations;

// ARCHITECTURE.md Section 5: "X-Correlation-ID on every call, passed on to every downstream call."
// CorrelationIdMiddleware establishes the id for an inbound HTTP request; this handler puts it on every
// outgoing call to OrchestratorApi/LedgerApi. Outside an HTTP request (the scheduler's own background
// tick), HttpContext is null and no header is added - OrchestratorApi/LedgerApi then generate their own,
// same as any request that arrives with no incoming id.
public class CorrelationIdPropagationHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = httpContextAccessor.HttpContext?.TraceIdentifier;
        if (!string.IsNullOrEmpty(correlationId))
        {
            request.Headers.Remove(CorrelationIdMiddleware.HeaderName);
            request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
