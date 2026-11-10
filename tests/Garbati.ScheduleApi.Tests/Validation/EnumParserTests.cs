using Garbati.ScheduleApi.Entities;
using Garbati.ScheduleApi.Validation;

namespace Garbati.ScheduleApi.Tests.Validation;

public class EnumParserTests
{
    [Theory]
    [InlineData("Savings", AccountType.Savings)]
    [InlineData("savings", AccountType.Savings)]
    [InlineData("LOAN", AccountType.Loan)]
    public void Parse_AcceptsNames_CaseInsensitively(string value, AccountType expected)
    {
        Assert.Equal(expected, EnumParser.Parse<AccountType>(value));
    }

    [Fact]
    public void Parse_RejectsTheNumericOrdinal_EvenThoughEnumTryParseWouldAccept_It()
    {
        // The exact LedgerApi PR #31 lesson: Enum.TryParse("1", ...) succeeds and returns AccountType.Loan,
        // but no real caller sends "1" for a string-shaped enum field - this helper deliberately does not
        // accept it either.
        Assert.True(Enum.TryParse<AccountType>("1", ignoreCase: true, out _));

        Assert.Null(EnumParser.Parse<AccountType>("1"));
    }

    [Fact]
    public void Parse_ReturnsNull_ForAnUnrecognizedValue()
    {
        Assert.Null(EnumParser.Parse<AccountType>("Current"));
    }

    [Fact]
    public void Parse_ReturnsNull_ForNull()
    {
        Assert.Null(EnumParser.Parse<AccountType>(null));
    }
}
