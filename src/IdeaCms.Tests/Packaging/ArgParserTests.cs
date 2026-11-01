using IdeaCms.Packaging;

namespace IdeaCms.Tests;

/// <summary>ArgParser — the CLI's minimal --key value argument parser (no external dependency).</summary>
[TestFixture]
public class ArgParserTests
{
    [Test]
    public void Parse_KeyValue_IsExtracted()
    {
        var map = ArgParser.Parse(new[] { "--path", "some/dir" });
        Assert.That(map["path"], Is.EqualTo("some/dir"));
    }

    [Test]
    public void Parse_FlagWithoutValue_SetsTrue()
    {
        var map = ArgParser.Parse(new[] { "--verbose" });
        Assert.That(map["verbose"], Is.EqualTo("true"));
    }

    [Test]
    public void Parse_LookupIsCaseInsensitive()
    {
        var map = ArgParser.Parse(new[] { "--Path", "x" });
        Assert.That(map.ContainsKey("path"), Is.True);
    }

    [Test]
    public void Parse_EmptyArgs_ReturnsEmptyMap()
    {
        Assert.That(ArgParser.Parse(Array.Empty<string>()), Is.Empty);
    }
}
