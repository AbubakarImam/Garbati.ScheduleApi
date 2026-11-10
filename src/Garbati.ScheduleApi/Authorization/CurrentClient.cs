namespace Garbati.ScheduleApi.Authorization;

// The API client making the current request (the client id from its API key) - needed to scope
// create-time idempotency keys per calling client, the same convention LedgerApi's AccountService and
// Garbati.IdentityApi's CustomerService both use. Null outside an authenticated request (e.g. in service
// tests or the scheduler's own background work, which never calls CreateAsync).
public interface ICurrentClient
{
    string? ClientId { get; }
}

public class HttpCurrentClient(IHttpContextAccessor httpContextAccessor) : ICurrentClient
{
    public string? ClientId => httpContextAccessor.HttpContext?.User.Identity?.Name;
}
