using System.Net;
using System.Text.Json;

namespace Garbati.ScheduleApi.Clients.Implementations;

// Shared response-reading for both downstream clients. Mirrors
// Garbati.OrchestratorApi.Clients.Implementations.DownstreamResponseReader exactly: OrchestratorApi and
// LedgerApi both follow ARCHITECTURE.md Section 5's error mapping, but a 400 body is a plain JSON array
// of strings (BadRequest(errors)), while every other error status is application/problem+json.
internal static class DownstreamResponseReader
{
    // Both services' ProblemDetails bodies use camelCase JSON property names against this type's
    // PascalCase properties - case-insensitive matching is required, not optional.
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<ProblemDetailsBody?> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var errors = JsonSerializer.Deserialize<List<string>>(body, JsonOptions);
                return errors is { Count: > 0 } ? new ProblemDetailsBody { Detail = string.Join("; ", errors) } : null;
            }

            return JsonSerializer.Deserialize<ProblemDetailsBody>(body, JsonOptions);
        }
        catch (JsonException)
        {
            // Not JSON at all (e.g. an upstream proxy's HTML error page) - fall back to the raw text.
            return new ProblemDetailsBody { Detail = body };
        }
    }

    public static string DescribeFailure(ProblemDetailsBody? problem, HttpResponseMessage response) =>
        problem?.Detail ?? problem?.Title ?? response.ReasonPhrase ?? "(no detail)";

    // Whether this response came from ExceptionHandlingMiddleware (a thrown, unexpected-shape error) as
    // opposed to a controller's own normal JSON body. OrchestratorApi's POST /v1/transfers returns a real
    // TransferResultResponse body on BOTH 200 and 422 (Status="Failed" is a legitimate business outcome,
    // not an exception) - but a 422 can ALSO come from AuthorizationDeniedException, mapped by that
    // service's own ExceptionHandlingMiddleware to a ProblemDetails body with no Reference/Status fields
    // at all. Content-type is what tells the two apart; relying on status code alone would silently
    // deserialize a ProblemDetails body into a TransferResultResponse with every field null.
    public static bool IsProblemDetails(HttpResponseMessage response) =>
        response.Content.Headers.ContentType?.MediaType == "application/problem+json";
}
