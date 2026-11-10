namespace Garbati.ScheduleApi.Contracts.Requests;

// Marks a request that carries a client idempotency key, so it can be logged without logging the body.
// Mirrors Garbati.OrchestratorApi.Contracts.Requests.IIdempotentRequest.
public interface IIdempotentRequest
{
    string IdempotencyKey { get; }
}
