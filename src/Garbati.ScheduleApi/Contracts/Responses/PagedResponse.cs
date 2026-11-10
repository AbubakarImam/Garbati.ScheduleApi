namespace Garbati.ScheduleApi.Contracts.Responses;

// Mirrors Garbati.IdentityApi.Contracts.Responses.PagedResponse<T> exactly - that type's own comment
// says it is kept deliberately generic so "the next list endpoint on any Garbati service... can reuse
// the exact same shape rather than inventing a slightly different one." This is ScheduleApi's own copy
// (never a cross-repo reference), reusing the convention, not the type.
//
// NextPageToken is an opaque string (callers must not parse or construct it) and is null exactly when
// there is no further page.
public record PagedResponse<T>(IReadOnlyList<T> Items, string? NextPageToken);
