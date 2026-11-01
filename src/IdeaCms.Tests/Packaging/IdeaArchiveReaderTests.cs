using IdeaCms.Packaging;

namespace IdeaCms.Tests.Packaging;

[TestFixture]
public class IdeaArchiveReaderTests
{
    [Test]
    public void ReadsManifestAndListsEntries_WithoutExtraction()
    {
        using var stream = IdeaTestArchive.CodePackage(key: "ui.tooltip", version: 1);
        using var reader = IdeaArchiveReader.Open(stream);

        Assert.That(reader.TryReadManifest(out var m, out var err), Is.True, err);
        Assert.That(m!.Key, Is.EqualTo("ui.tooltip"));
        Assert.That(reader.BinEntries(), Is.EquivalentTo(new[] { "Demo.dll" }));
        Assert.That(reader.WwwrootEntries(), Is.EquivalentTo(new[] { "css/x.css" }));
    }

    [Test]
    public void MissingManifest_TryReadReturnsFalseWithError()
    {
        using var stream = IdeaTestArchive.Build(new Dictionary<string, string> { ["bin/Demo.dll"] = "x" });
        using var reader = IdeaArchiveReader.Open(stream);
        Assert.That(reader.TryReadManifest(out var m, out var err), Is.False);
        Assert.That(m, Is.Null);
        Assert.That(err, Does.Contain("idea.json"));
    }

    [Test]
    public void EmptyDataPackage_ManifestOnly_IsLegal()
    {
        using var stream = IdeaTestArchive.Build(new Dictionary<string, string>
        {
            ["idea.json"] = ManifestReader.Write(new IdeaManifest
            { ManifestVersion = 1, Category = "Page", Kind = "data", Key = "about", Version = 1, DisplayName = "About" }),
        });
        using var reader = IdeaArchiveReader.Open(stream);
        Assert.That(reader.TryReadManifest(out _, out _), Is.True);
        Assert.That(reader.BinEntries(), Is.Empty);
        Assert.That(reader.WwwrootEntries(), Is.Empty);
    }

    [TestCase("../evil.dll", false)]
    [TestCase("/etc/passwd", false)]
    [TestCase("C:/Windows/x.dll", false)]
    [TestCase("bin\\..\\..\\x", false)]
    [TestCase("bin/Demo.dll", true)]
    [TestCase("wwwroot/css/x.css", true)]
    public void IsSafeEntryPath_RejectsZipSlip(string path, bool safe)
    {
        Assert.That(IdeaArchiveReader.IsSafeEntryPath(path), Is.EqualTo(safe));
    }
}
