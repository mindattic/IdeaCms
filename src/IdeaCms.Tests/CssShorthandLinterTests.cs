using IdeaCms.Core.Services;

namespace IdeaCms.Tests;

[TestFixture]
public class CssShorthandLinterTests
{
    [Test]
    public void ShorthandWithOneLonghandSibling_Warns()
    {
        var warnings = CssShorthandLinter.Scan(".a{margin: 10px;} .b{margin-left: 5px;}");

        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0].Shorthand, Is.EqualTo("margin"));
        Assert.That(warnings[0].CoOccurringLonghands, Does.Contain("margin-left"));
    }

    [Test]
    public void ShorthandAlone_DoesNotWarn()
    {
        var warnings = CssShorthandLinter.Scan(".a{margin: 10px;}");

        Assert.That(warnings, Is.Empty);
    }

    [Test]
    public void ShorthandWithAllLonghandSiblings_DoesNotWarn()
    {
        var warnings = CssShorthandLinter.Scan(
            ".a{margin: 10px; margin-top:1px; margin-right:1px; margin-bottom:1px; margin-left:1px;}");

        Assert.That(warnings, Is.Empty);
    }

    [Test]
    public void ShorthandInsideBlockComment_IsIgnored()
    {
        var warnings = CssShorthandLinter.Scan("/* margin: 10px; */ .b{margin-left: 5px;}");

        Assert.That(warnings, Is.Empty);
    }

    [Test]
    public void ShorthandInsideStringLiteral_IsIgnored()
    {
        var warnings = CssShorthandLinter.Scan(".a::after{content: \"margin: 10px;\"} .b{margin-left: 3px;}");

        Assert.That(warnings, Is.Empty);
    }

    [Test]
    public void MultipleIndependentShorthands_EachProduceTheirOwnWarning()
    {
        var warnings = CssShorthandLinter.Scan(
            ".a{margin: 10px; margin-left: 2px; background: red; background-color: blue;}");

        Assert.That(warnings, Has.Count.EqualTo(2));
        Assert.That(warnings.Select(w => w.Shorthand), Is.EquivalentTo(new[] { "margin", "background" }));
    }

    [Test]
    public void PropertyMatching_IsCaseInsensitive()
    {
        var warnings = CssShorthandLinter.Scan(".a{MARGIN: 10px;} .b{margin-Left: 2px;}");

        Assert.That(warnings, Has.Count.EqualTo(1));
    }

    [Test]
    public void NullOrBlankCss_ReturnsNoWarnings()
    {
        Assert.That(CssShorthandLinter.Scan(null), Is.Empty);
        Assert.That(CssShorthandLinter.Scan("   "), Is.Empty);
    }
}
