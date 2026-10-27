using Xunit;

namespace KeyboardLayoutSwitcher.Tests;

public class LayoutsTests
{
    [Theory]
    [InlineData("en-US", "US")]
    [InlineData("nl-BE", "BE")]
    [InlineData("sr-Latn-RS", "RS")]
    [InlineData("de", "DE")]
    public void CultureCode_TakesRegion(string culture, string expected)
    {
        Assert.Equal(expected, Layouts.CultureCode(culture));
    }

    [Fact]
    public void ShortCodes_NumbersRepeatedCodes()
    {
        var codes = Layouts.ShortCodes(["en-US", "nl-BE", "en-US", "es-US"]);

        Assert.Equal(["US", "BE", "US2", "US3"], codes);
    }

    [Fact]
    public void ShortCodes_UniqueCodesUnchanged()
    {
        Assert.Equal(["US", "BE"], Layouts.ShortCodes(["en-US", "fr-BE"]));
    }
}
