namespace Garbati.ScheduleApi.Exceptions;

// Thrown by the OrchestratorApi/LedgerApi HTTP clients for any response ScheduleApi does not know how to
// treat as a specific, named outcome. Mirrors Garbati.OrchestratorApi.Exceptions.DownstreamApiException
// exactly. In the scheduler (Scheduling/StandingInstructionRunner), this is caught and recorded as a
// Failed run with this exception's Message, never a crash of the whole tick.
public class DownstreamApiException(string serviceName, int statusCode, string detail)
    : Exception($"{serviceName} returned {statusCode}: {detail}")
{
    public string ServiceName { get; } = serviceName;
    public int StatusCode { get; } = statusCode;
}
