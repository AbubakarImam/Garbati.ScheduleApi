namespace Garbati.ScheduleApi.Exceptions;

// Same key, different request body - refused rather than silently replaying the wrong resource (409).
// Mirrors LedgerApi's/Garbati.IdentityApi's exception of the same name exactly.
public class DuplicateIdempotencyKeyException : Exception
{
    public DuplicateIdempotencyKeyException()
    {
    }

    public DuplicateIdempotencyKeyException(string message)
        : base(message)
    {
    }

    public DuplicateIdempotencyKeyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
