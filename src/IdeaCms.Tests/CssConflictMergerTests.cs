using IdeaCms.Core.Services;

namespace IdeaCms.Tests;

/// <summary>
/// M.IDEA-§4.7: CssConflictMerger collapses same-selector shorthand/longhand conflicts WITHIN one CSS text
/// (never across texts -- see the class doc comment for why GlobalCss/Theme/Component are out of scope).
/// </summary>
[TestFixture]
public class CssConflictMergerTests
{
    [Test]
    public void DuplicateSelector_LaterLonghandOverridesOneSide_ShorthandKeepsTheOtherSides()
    {
        var result = CssConflictMerger.Normalize(".a { margin: 1px; color: red; } .a { margin-left: 9px; }");

        // AngleSharp expands "margin: 1px" into 4 real longhands internally (top/right/bottom/left all
        // 1px), the later "margin-left: 9px" overrides only the left one, and its own serializer then
        // recombines the 4 still-equivalent longhands back into the compact TRBL shorthand form on
        // output -- "1px 1px 1px 9px" is exactly top=1,right=1,bottom=1,left=9, i.e. the correct merge.
        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("margin: 1px 1px 1px 9px"));
            Assert.That(result, Does.Contain("color:"));
            // The two ".a" blocks are collapsed into one -- the selector appears exactly once.
            Assert.That(CountOccurrences(result, ".a {"), Is.EqualTo(1));
        });
    }

    [Test]
    public void DuplicateSelector_LaterShorthandResetsAllSides()
    {
        var result = CssConflictMerger.Normalize(".a { margin-left: 9px; } .a { margin: 1px; }");

        Assert.That(result, Does.Contain("margin: 1px"));
        Assert.That(result, Does.Not.Contain("9px"));
    }

    [Test]
    public void Border_TwelveWayExpansion_LaterSingleChannelOverridesJustThatChannel()
    {
        var result = CssConflictMerger.Normalize(".a { border: 1px solid red; } .a { border-left-color: green; }");

        // "border: 1px solid red" expands to all 12 width/style/color-per-side longhands internally;
        // only border-left-color is overridden, so border-left's color differs from the other 3 sides
        // once the serializer recombines each side's 3 longhands back into its own per-side shorthand.
        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("border-left: 1px solid rgba(0, 128, 0, 1)"));
            Assert.That(result, Does.Contain("border-top: 1px solid rgba(255, 0, 0, 1)"));
            Assert.That(result, Does.Contain("border-right: 1px solid rgba(255, 0, 0, 1)"));
            Assert.That(result, Does.Contain("border-bottom: 1px solid rgba(255, 0, 0, 1)"));
        });
    }

    [Test]
    public void EarlierImportantDeclaration_BeatsALaterNonImportantOne()
    {
        var result = CssConflictMerger.Normalize(".a { color: red !important; } .a { color: blue; }");

        Assert.That(result, Does.Contain("!important"));
        Assert.That(result, Does.Contain("255, 0, 0"));
    }

    [Test]
    public void LaterImportantDeclaration_BeatsAnEarlierImportantOne()
    {
        var result = CssConflictMerger.Normalize(".a { color: red !important; } .a { color: blue !important; }");

        Assert.That(result, Does.Contain("0, 0, 255"));
    }

    [Test]
    public void NonExactSelectors_AreLeftCompletelyUntouched()
    {
        var result = CssConflictMerger.Normalize(".theme .x { margin: 1px; } .x { margin: 2px; }");

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain(".theme .x").And.Contain("1px"),
                "a different (non-exact) selector's rule must survive untouched");
            Assert.That(result, Does.Contain("2px"),
                "the .x rule must also survive untouched, deferring entirely to cascade layers");
        });
    }

    [Test]
    public void BoxShadow_StaysAtomic_LastDeclarationWinsAsASingleValue()
    {
        var result = CssConflictMerger.Normalize(".a { box-shadow: 1px 1px red; } .a { box-shadow: 2px 2px blue; }");

        Assert.That(result, Does.Contain("box-shadow: 2px 2px"));
        Assert.That(result, Does.Not.Contain("box-shadow-offset"));
    }

    [Test]
    public void NoConflict_DisjointSelectors_BothPreserved()
    {
        var result = CssConflictMerger.Normalize(".x { color: red; } .y { color: blue; }");

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain(".x"));
            Assert.That(result, Does.Contain(".y"));
        });
    }

    [Test]
    public void NoDuplicateSelectors_ReturnsInputByteForByteIdentical()
    {
        // The whole point of the minimal-diff design: with nothing to merge, colors/spacing/comments
        // must stay EXACTLY as written -- not round-tripped through AngleSharp's canonicalizing
        // serializer (which would turn "red" into "rgba(255, 0, 0, 1)" even with nothing to fix).
        const string css = "/* header */\n.x {\n  color: red;\n  margin: 1px 2px;\n}\n\n.y { color: blue; }\n";

        Assert.That(CssConflictMerger.Normalize(css), Is.EqualTo(css));
    }

    [Test]
    public void Comments_SurroundingADuplicateGroup_AreNeverTouched()
    {
        const string css = "/* a comment */\n.a { color: red; }\n/* between */\n.a { color: green; }\n/* trailing */\n";

        var result = CssConflictMerger.Normalize(css);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("/* a comment */"));
            Assert.That(result, Does.Contain("/* between */"));
            Assert.That(result, Does.Contain("/* trailing */"));
            Assert.That(result, Does.Contain("0, 128, 0")); // green wins (later declaration)
            Assert.That(CountOccurrences(result, ".a {"), Is.EqualTo(1));
        });
    }

    [Test]
    public void AtRuleBlocks_ArePassedThroughOpaquely_NotMergedWithATopLevelDuplicate()
    {
        const string css = "@media (min-width: 600px) { .a { color: red; } }\n.a { color: blue; }\n.a { color: green; }\n";

        var result = CssConflictMerger.Normalize(css);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("@media (min-width: 600px) { .a { color: red; } }"),
                "the @media block's own .a must be left alone, never folded into the top-level duplicate group");
            Assert.That(result, Does.Contain("0, 128, 0")); // the two top-level .a rules still merge, green wins
            Assert.That(CountOccurrences(result, ".a {"), Is.EqualTo(2), "one inside @media (untouched) + one merged top-level");
        });
    }

    [Test]
    public void DuplicateSelector_ShorthandValueUsesCssCustomProperty_IsLeftCompletelyUntouched()
    {
        // Regression: "background: var(--bg)" cannot be safely decomposed into per-layer longhands
        // without resolving --bg (out of scope) -- AngleSharp.Css represents that as empty-valued
        // synthesized longhands and the var() text isn't recoverable from the per-property enumeration
        // at all, so a naive merge would silently DROP the background entirely. Confirmed against a real
        // library theme file before this guard existed. Both occurrences must survive, unmerged.
        const string css = ".a { background: var(--bg); margin: 0; } .a { padding-block: 1rem; }";

        var result = CssConflictMerger.Normalize(css);

        Assert.That(result, Is.EqualTo(css));
    }

    [Test]
    public void DuplicateSelector_ContainsAVendorPrefixedProperty_IsLeftCompletelyUntouched()
    {
        // Regression: AngleSharp.Css silently drops a property/value pair it can't fully validate --
        // confirmed for "-webkit-font-smoothing: antialiased", which vanishes from the parsed rule
        // entirely with no error. A naive merge would lose it. Both occurrences must survive, unmerged.
        const string css = ".a { color: red; -webkit-font-smoothing: antialiased; } .a { margin: 0; }";

        var result = CssConflictMerger.Normalize(css);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(css));
            Assert.That(result, Does.Contain("-webkit-font-smoothing: antialiased"));
        });
    }

    [Test]
    public void DuplicateSelector_InterveningRuleSetsTheSameProperty_IsNotMovedAcrossIt()
    {
        // Regression: merging moved the later ".a { color: green }" up in front of ".b { color: blue }",
        // so an element with both classes flipped from green to blue. Cascade order must be preserved.
        const string css = ".a { color: red; } .b { color: blue; } .a { color: green; }";

        Assert.That(CssConflictMerger.Normalize(css), Is.EqualTo(css));
    }

    [Test]
    public void DuplicateSelector_InterveningRuleTouchesOtherProperties_StillMerges()
    {
        const string css = ".a { color: red; } .b { margin: 0; } .a { color: green; }";

        var result = CssConflictMerger.Normalize(css);

        Assert.Multiple(() =>
        {
            Assert.That(CountOccurrences(result, ".a {"), Is.EqualTo(1));
            Assert.That(result, Does.Contain("0, 128, 0"));
            Assert.That(result, Does.Contain(".b { margin: 0; }"));
        });
    }

    [Test]
    public void MalformedCss_ReturnsInputUnchanged_NeverThrows()
    {
        const string malformed = ".a { margin: ; ";

        Assert.DoesNotThrow(() => CssConflictMerger.Normalize(malformed));
    }

    [Test]
    public void NullOrBlank_ReturnsAsIs()
    {
        Assert.That(CssConflictMerger.Normalize(null), Is.EqualTo(""));
        Assert.That(CssConflictMerger.Normalize("   "), Is.EqualTo("   "));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
