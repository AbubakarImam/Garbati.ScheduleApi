using Garbati.ScheduleApi.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Garbati.ScheduleApi.Middleware;

// Same shape as every sibling service's middleware of the same name (ARCHITECTURE.md Section 5's
// status-code mapping: 404 not found, 409 conflict, 422 business-rule failure, 500 unexpected; 400/401/403
// are handled per-controller/by ApiKeyAuthenticationHandler, not here).
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request cancelled by client: {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var (status, title) = Map(ex);
            var isExpected = status != StatusCodes.Status500InternalServerError;

            if (isExpected)
                logger.LogWarning(ex, "Request rejected ({Status}) {Method} {Path}", status, context.Request.Method, context.Request.Path);
            else
                logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.StatusCode = status;

            var problem = new ProblemDetails
            {
                Status = status,
                Title = title,
                // Only expected business errors expose their message; unexpected ones must not leak internals.
                Detail = isExpected ? ex.Message : null,
                Extensions = { ["correlationId"] = context.TraceIdentifier }
            };

            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
        }
    }

    private static (int Status, string Title) Map(Exception ex) => ex switch
    {
        StandingInstructionNotFoundException => (StatusCodes.Status404NotFound, "Standing instruction not found."),
        DuplicateStandingInstructionNameException => (StatusCodes.Status409Conflict, "Duplicate standing instruction name."),
        StandingInstructionDeletedException => (StatusCodes.Status422UnprocessableEntity, "Standing instruction deleted."),
        NoFinancingModuleException => (StatusCodes.Status422UnprocessableEntity, "Requires a financing module that does not exist yet."),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
    };
}
