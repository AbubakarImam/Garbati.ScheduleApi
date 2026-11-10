using Garbati.ScheduleApi.Authorization;
using Garbati.ScheduleApi.Contracts.Requests;
using Garbati.ScheduleApi.Contracts.Responses;
using Garbati.ScheduleApi.Services;
using Garbati.ScheduleApi.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Garbati.ScheduleApi.Controllers;

// docs/DESIGN.md Section 4: all 7 endpoints, under /v1/standing-instructions.
[ApiController]
[Route("v1/standing-instructions")]
public class StandingInstructionsController(
    IStandingInstructionService standingInstructionService,
    CreateStandingInstructionRequestValidator createValidator,
    UpdateStandingInstructionRequestValidator updateValidator,
    UpdateStandingInstructionStatusRequestValidator updateStatusValidator) : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    [Authorize(Policy = ScheduleApiScopes.Write)]
    [HttpPost]
    public async Task<ActionResult<StandingInstructionResponse>> Create(
        [FromBody] CreateStandingInstructionRequest request, CancellationToken cancellationToken)
    {
        var errors = createValidator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        // Rejections for the not-yet-supported financing-related combinations (Loan account type,
        // non-AccountTransfer, non-Fixed, non-Periodic) are thrown as NoFinancingModuleException from the
        // service and mapped to 422 by ExceptionHandlingMiddleware - never reached as a normal return
        // here, same as AuthorizationDeniedException never reaching TransfersController.Transfer's own
        // return statement in Garbati.OrchestratorApi.
        var response = await standingInstructionService.CreateAsync(request, cancellationToken);
        return Ok(response);
    }

    [Authorize(Policy = ScheduleApiScopes.Read)]
    [HttpGet("{id}")]
    public async Task<ActionResult<StandingInstructionResponse>> GetById(string id, CancellationToken cancellationToken)
    {
        var response = await standingInstructionService.GetAsync(id, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [Authorize(Policy = ScheduleApiScopes.Read)]
    [HttpGet]
    public async Task<ActionResult<PagedResponse<StandingInstructionResponse>>> List(
        [FromQuery] string? customerId, [FromQuery] int? pageSize, [FromQuery] string? pageToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return BadRequest(new[] { "customerId is required." });
        }

        var effectivePageSize = pageSize ?? DefaultPageSize;
        if (effectivePageSize < 1 || effectivePageSize > MaxPageSize)
        {
            return BadRequest(new[] { $"pageSize must be between 1 and {MaxPageSize}." });
        }

        var response = await standingInstructionService.ListAsync(customerId, effectivePageSize, pageToken, cancellationToken);
        return Ok(response);
    }

    // Real in-place edit (docs/DESIGN.md Section 4) - amount, schedule, priority, AllowPartialTransfer
    // only (Contracts/Requests/UpdateStandingInstructionRequest's own comment on why it is narrower than
    // the create request). 404/422 come from StandingInstructionService throwing
    // StandingInstructionNotFoundException/StandingInstructionDeletedException/NoFinancingModuleException,
    // caught by ExceptionHandlingMiddleware - not a null check or an if/return here.
    [Authorize(Policy = ScheduleApiScopes.Write)]
    [HttpPut("{id}")]
    public async Task<ActionResult<StandingInstructionResponse>> Update(
        string id, [FromBody] UpdateStandingInstructionRequest request, CancellationToken cancellationToken)
    {
        var errors = updateValidator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        var response = await standingInstructionService.UpdateAsync(id, request, cancellationToken);
        return Ok(response);
    }

    // Soft delete (Status -> Deleted, docs/DESIGN.md Section 4) - returns the now-Deleted resource, the
    // same "return the resource" convention every status-changing endpoint on this platform uses
    // (Garbati.IdentityApi's SuspendCustomer/ReactivateCustomer), not 204 No Content.
    [Authorize(Policy = ScheduleApiScopes.Write)]
    [HttpDelete("{id}")]
    public async Task<ActionResult<StandingInstructionResponse>> Delete(string id, CancellationToken cancellationToken)
    {
        var response = await standingInstructionService.DeleteAsync(id, cancellationToken);
        return Ok(response);
    }

    [Authorize(Policy = ScheduleApiScopes.Write)]
    [HttpPatch("{id}/status")]
    public async Task<ActionResult<StandingInstructionResponse>> UpdateStatus(
        string id, [FromBody] UpdateStandingInstructionStatusRequest request, CancellationToken cancellationToken)
    {
        var errors = updateStatusValidator.Validate(request);
        if (errors.Count > 0)
        {
            return BadRequest(errors);
        }

        var response = await standingInstructionService.UpdateStatusAsync(id, request, cancellationToken);
        return Ok(response);
    }

    [Authorize(Policy = ScheduleApiScopes.Read)]
    [HttpGet("{id}/runs")]
    public async Task<ActionResult<PagedResponse<StandingInstructionRunResponse>>> ListRuns(
        string id, [FromQuery] int? pageSize, [FromQuery] string? pageToken, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize ?? DefaultPageSize;
        if (effectivePageSize < 1 || effectivePageSize > MaxPageSize)
        {
            return BadRequest(new[] { $"pageSize must be between 1 and {MaxPageSize}." });
        }

        var response = await standingInstructionService.ListRunsAsync(id, effectivePageSize, pageToken, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }
}
