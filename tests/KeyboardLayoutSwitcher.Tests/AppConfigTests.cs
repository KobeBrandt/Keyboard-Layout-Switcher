using Xunit;

namespace KeyboardLayoutSwitcher.Tests;

public class AppConfigTests
{
    private static readonly LayoutRef Us = new() { Culture = "en-US", LayoutName = "US" };
    private static readonly LayoutRef Belgian = new() { Culture = "nl-BE", LayoutName = "Belgian (Period)" };
    private static readonly LayoutRef Missing = new() { Culture = "fr-FR", LayoutName = "French" };

    private static AppConfig Config(params KeyboardRule[] rules) => new() { Keyboards = rules.ToList() };

    private static KeyboardRule Rule(string id, LayoutRef layout) => new() { Name = id, HardwareId = id, Layout = layout };

    private static bool Installed(LayoutRef layout) => layout != Missing;

    [Fact]
    public void PickRule_NothingConnected_ReturnsDefault()
    {
        var config = Config(Rule("A", Us));

        Assert.Null(config.PickRule(new HashSet<string>(), Installed));
    }

    [Fact]
    public void PickRule_ConnectedKeyboard_Wins()
    {
        var config = Config(Rule("A", Us));

        Assert.Equal("A", config.PickRule(new HashSet<string> { "A", "OTHER" }, Installed)?.HardwareId);
    }

    [Fact]
    public void PickRule_SeveralConnected_HighestInListWins()
    {
        var config = Config(Rule("A", Us), Rule("B", Belgian));

        Assert.Equal("A", config.PickRule(new HashSet<string> { "B", "A" }, Installed)?.HardwareId);
    }

    [Fact]
    public void PickRule_SkipsKeyboardWhoseLayoutIsNotInstalled()
    {
        var config = Config(Rule("A", Missing), Rule("B", Belgian));

        Assert.Equal("B", config.PickRule(new HashSet<string> { "A", "B" }, Installed)?.HardwareId);
    }

    [Fact]
    public void PickRule_OnlyUninstalledLayoutConnected_ReturnsDefault()
    {
        var config = Config(Rule("A", Missing));

        Assert.Null(config.PickRule(new HashSet<string> { "A" }, Installed));
    }

    [Fact]
    public void Clone_IsIndependentCopy()
    {
        var config = Config(Rule("A", Us));
        config.DefaultLayout = Belgian;
        config.EnableLog = true;

        var clone = config.Clone();
        clone.Keyboards[0].Name = "Changed";

        Assert.Equal("A", config.Keyboards[0].Name);
        Assert.Equal("nl-BE", clone.DefaultLayout?.Culture);
        Assert.True(clone.EnableLog);
    }
}
