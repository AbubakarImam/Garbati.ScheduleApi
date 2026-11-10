using Garbati.ScheduleApi.Authorization;
using Garbati.ScheduleApi.Clients.Implementations;
using Garbati.ScheduleApi.Clients.Interfaces;
using Garbati.ScheduleApi.Configuration;
using Garbati.ScheduleApi.Data;
using Garbati.ScheduleApi.Middleware;
using Garbati.ScheduleApi.Scheduling;
using Garbati.ScheduleApi.Services;
using Garbati.ScheduleApi.Services.Implementations;
using Garbati.ScheduleApi.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

// Bootstrap logger: records errors that happen before configuration is loaded (e.g. a broken appsettings file).
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Replace the default logging with Serilog, configured from the "Serilog" section of appsettings.
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddControllers(options => options.Filters.Add<IdempotencyKeyLoggingFilter>());
    // Lowercase route URLs in generated links and the Swagger docs, same as every sibling service.
    builder.Services.AddRouting(options => options.LowercaseUrls = true);
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        // Adds an "Authorize" button to Swagger UI that sends the X-Api-Key header - this is the key a
        // caller of THIS service presents, not either downstream service's key.
        options.AddSecurityDefinition(ApiKeyAuthenticationHandler.SchemeName, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyAuthenticationHandler.HeaderName,
            Description = "API key issued to the calling client."
        });
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = ApiKeyAuthenticationHandler.SchemeName }
            }] = []
        });
    });

    // Authentication: who is calling THIS service (API key -> client id + scopes). Same pattern as
    // every sibling service.
    builder.Services.AddOptions<ApiKeyOptions>()
        .Bind(builder.Configuration.GetSection("ApiKeys"))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<ApiKeyOptions>, ApiKeyOptionsValidator>();

    // This service's OWN outbound credentials for calling OrchestratorApi/LedgerApi (Configuration/
    // DownstreamServicesOptions.cs) - a different concern from ApiKeys above.
    builder.Services.AddOptions<DownstreamServicesOptions>()
        .Bind(builder.Configuration.GetSection("DownstreamServices"))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<DownstreamServicesOptions>, DownstreamServicesOptionsValidator>();

    builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);

    // Authorization: one policy per scope, plus a fallback so an endpoint without [Authorize] is never public.
    builder.Services.AddAuthorization(options =>
    {
        foreach (var scope in ScheduleApiScopes.All)
            options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser().RequireClaim(ScheduleApiScopes.ClaimType, scope));

        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    });

    builder.Services.AddDbContext<ScheduleDbContext>(options => options
        .UseNpgsql(builder.Configuration.GetConnectionString("ScheduleDb"))
        .UseSnakeCaseNamingConvention());

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentClient, HttpCurrentClient>();

    // Health checks for the hosting platform: "ready" includes the database, "live" checks only the process.
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<ScheduleDbContext>("database", tags: ["ready"]);

    builder.Services.AddScoped<IStandingInstructionService, StandingInstructionService>();
    builder.Services.AddScoped<IStandingInstructionRunner, StandingInstructionRunner>();

    builder.Services.AddScoped<CreateStandingInstructionRequestValidator>();
    builder.Services.AddScoped<UpdateStandingInstructionRequestValidator>();
    builder.Services.AddScoped<UpdateStandingInstructionStatusRequestValidator>();

    // This service as a client of OrchestratorApi/LedgerApi. BaseAddress and the outgoing X-Api-Key value
    // come from DownstreamServicesOptions, resolved once per HttpClient instance from the pool.
    builder.Services.AddTransient<CorrelationIdPropagationHandler>();

    builder.Services.AddHttpClient<IOrchestratorApiClient, OrchestratorApiClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<DownstreamServicesOptions>>().Value.OrchestratorApi;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, options.ApiKey);
    }).AddHttpMessageHandler<CorrelationIdPropagationHandler>();

    builder.Services.AddHttpClient<ILedgerApiClient, LedgerApiClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<DownstreamServicesOptions>>().Value.LedgerApi;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, options.ApiKey);
    }).AddHttpMessageHandler<CorrelationIdPropagationHandler>();

    // docs/DESIGN.md Section 3: the scheduling mechanism itself. Disabled under "Testing" so the full
    // ApiFixture test suite (HTTP-level endpoint tests) never races a real background tick against its
    // own test data - Scheduling/StandingInstructionRunnerTests exercises the scheduling logic directly
    // and does not need the hosted service running.
    if (!builder.Environment.IsEnvironment("Testing"))
    {
        builder.Services.AddHostedService<StandingInstructionSchedulerService>();
    }

    var app = builder.Build();

    // First in the pipeline, so every later log line (including the request summary) has the id.
    app.UseMiddleware<CorrelationIdMiddleware>();

    // One structured log line per request: method, path, status code and elapsed time, plus these extras.
    app.UseSerilogRequestLogging(options =>
    {
        // Platforms probe /health every few seconds; log those only when they fail.
        options.GetLevel = (httpContext, _, exception) =>
            exception is not null || httpContext.Response.StatusCode >= 500 ? LogEventLevel.Error
            : httpContext.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose
            : LogEventLevel.Information;

        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("UserId", httpContext.User.Identity?.Name ?? "anonymous");
            diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());
        };
    });

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
    {
        app.UseSwagger();
        app.UseSwaggerUI(options => options.EnablePersistAuthorization());
    }

    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // No UseHttpsRedirection: a redirect cannot protect an API key that was already sent over HTTP. TLS
    // belongs to the deployment (proxy/ingress or Kestrel), same as every sibling service.

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // Anonymous on purpose: platforms probe without an API key. Live = the process responds; ready = it
    // can reach the database.
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Schedule API terminated unexpectedly");
    // Non-zero so Docker, Kubernetes and CI see a failed start rather than a clean stop.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

// Lets the test project start the app in memory with WebApplicationFactory<Program>.
public partial class Program;
