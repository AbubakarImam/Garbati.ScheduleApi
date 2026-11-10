using System.Text.RegularExpressions;

namespace Garbati.ScheduleApi.Validation;

// Same rule as every sibling service's IdempotencyKeyRule: short enough to always fit a unique index, and
// limited to characters that are safe in logs and URLs. The reserved prefix here is "sched:" - the same
// namespace Scheduling/StandingInstructionRunner already uses for its own derived run keys
// (sched:{id}:{date}, docs/DESIGN.md 3.1), so a caller cannot forge one of those either.
public static partial class IdempotencyKeyRule
{
    public const int MaxLength = 128;

    public const string ReservedPrefix = "sched:";

    // \z, not $: in .NET, $ also matches just before a trailing newline, so "abc\n" would pass.
    [GeneratedRegex(@"^[A-Za-z0-9._:-]{1,128}\z")]
    private static partial Regex Pattern();

    public static string? Validate(string? key) =>
        string.IsNullOrWhiteSpace(key) ? "IdempotencyKey is required."
        : !Pattern().IsMatch(key) ? $"IdempotencyKey must be 1-{MaxLength} characters: letters, digits, '-', '_', '.' or ':'."
        : key.StartsWith(ReservedPrefix, StringComparison.OrdinalIgnoreCase) ? $"IdempotencyKey must not start with '{ReservedPrefix}': it is reserved for this service's own derived run keys."
        : null;
}
