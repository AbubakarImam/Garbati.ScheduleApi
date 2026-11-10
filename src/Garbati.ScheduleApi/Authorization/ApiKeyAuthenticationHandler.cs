using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Garbati.ScheduleApi.Authorization;

// Authenticates the calling client from its X-Api-Key header - identical scheme/header to every sibling
// service (ARCHITECTURE.md Section 5: "API key plus scopes, hash-only configuration, one client per
// calling service"). This authenticates whoever calls ScheduleApi (ops tooling/Swagger); it is unrelated
// to ScheduleApi's own outgoing calls to OrchestratorApi/LedgerApi.
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<ApiKeyOptions> apiKeyOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No header: "not authenticated" rather than "failed"; the endpoint's policy decides what that means.
        if (!Request.Headers.TryGetValue(HeaderName, out var presented) || string.IsNullOrEmpty(presented))
            return Task.FromResult(AuthenticateResult.NoResult());

        var presentedHash = ApiKeyHasher.Hash(presented.ToString());
        var client = apiKeyOptions.CurrentValue.Clients.FirstOrDefault(c =>
            ApiKeyHasher.TryDecode(c.KeyHash, out var configured)
            && CryptographicOperations.FixedTimeEquals(configured, presentedHash));

        if (client is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        // Name = client id: this is what the request log's UserId picks up.
        var claims = new List<Claim> { new(ClaimTypes.Name, client.ClientId) };
        claims.AddRange(client.Scopes.Select(scope => new Claim(ScheduleApiScopes.ClaimType, scope)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = SchemeName;
        return WriteProblemAsync(StatusCodes.Status401Unauthorized, "A valid API key is required.");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        WriteProblemAsync(StatusCodes.Status403Forbidden, "This API key is not allowed to perform this operation.");

    // Same ProblemDetails shape (with correlationId) as ExceptionHandlingMiddleware's errors.
    private Task WriteProblemAsync(int status, string title)
    {
        Response.StatusCode = status;
        return Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions = { ["correlationId"] = Context.TraceIdentifier }
            },
            options: null,
            contentType: "application/problem+json");
    }
}
