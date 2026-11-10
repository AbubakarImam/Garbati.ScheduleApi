using Garbati.ScheduleApi.Contracts.Requests;
using Garbati.ScheduleApi.Contracts.Responses;

namespace Garbati.ScheduleApi.Services;

public interface IStandingInstructionService
{
    Task<StandingInstructionResponse> CreateAsync(CreateStandingInstructionRequest request, CancellationToken cancellationToken);

    Task<StandingInstructionResponse?> GetAsync(string standingInstructionId, CancellationToken cancellationToken);

    Task<PagedResponse<StandingInstructionResponse>> ListAsync(
        string customerId, int pageSize, string? pageToken, CancellationToken cancellationToken);

    Task<StandingInstructionResponse> UpdateAsync(
        string standingInstructionId, UpdateStandingInstructionRequest request, CancellationToken cancellationToken);

    Task<StandingInstructionResponse> DeleteAsync(string standingInstructionId, CancellationToken cancellationToken);

    Task<StandingInstructionResponse> UpdateStatusAsync(
        string standingInstructionId, UpdateStandingInstructionStatusRequest request, CancellationToken cancellationToken);

    Task<PagedResponse<StandingInstructionRunResponse>?> ListRunsAsync(
        string standingInstructionId, int pageSize, string? pageToken, CancellationToken cancellationToken);
}
