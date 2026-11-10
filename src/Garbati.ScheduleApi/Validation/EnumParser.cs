namespace Garbati.ScheduleApi.Validation;

// Every enum-shaped field on a request/response contract is a plain string on the wire (System.Text.Json's
// default enum handling only accepts the raw ordinal integer with no converter configured - exactly the
// bug LedgerApi PR #31 shipped with ChainKind, caught only by QA testing the real HTTP endpoint). Mirrors
// LedgerApi's own ChainRequestValidator.ParsePurpose/ParseChainKind split exactly, generalized to one
// helper since this service has seven such fields, not two: names only, case-insensitive (Enum.TryParse
// would also silently accept "1" as a numeric ordinal, which a real caller sending a string would never
// send and which this helper deliberately does not accept either).
public static class EnumParser
{
    public static TEnum? Parse<TEnum>(string? value) where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>().FirstOrDefault(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
            is { } match ? Enum.Parse<TEnum>(match) : null;
}
