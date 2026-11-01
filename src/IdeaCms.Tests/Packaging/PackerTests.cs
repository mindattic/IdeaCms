using IdeaCms.Packaging;

namespace IdeaCms.Tests.Packaging;

/// <summary>
/// Unit coverage for the packer's pure rules + its entry-type guard. Full end-to-end pack of a real
/// conforming RCL (which requires a built citizen project on disk) is exercised by the ma-idea CLI and
/// deferred to an attended integration run — it can't be reproduced reliably inside a unit-test process.
/// </summary>
[TestFixture]
public class PackerTests
{
    [TestCase("MindAttic.Ideas.Abstractions", true)]
    [TestCase("IdeaCms.Core", true)]
    [TestCase("Microsoft.AspNetCore.Components", true)]
    [TestCase("Microsoft.Extensions.DependencyInjection", true)]
    [TestCase("System.Text.Json", true)]
    [TestCase("mscorlib", true)]
    [TestCase("netstandard", true)]
    [TestCase("Markdig", false)]
    [TestCase("MyWidget", false)]
    public void IsHostAssembly_ExcludesHostAndFrameworkOnly(string name, bool isHost)
    {
        Assert.That(Packer.IsHostAssembly(name), Is.EqualTo(isHost));
    }

    [Test]
    public void Pack_AssemblyWithNoEntryType_ThrowsPackException()
    {
        // The Packaging assembly itself has no V<n> citizen type in a IdeaCms.<Kind>.<Key> namespace.
        var asmPath = typeof(IdeaManifest).Assembly.Location;
        var asmDir = Path.GetDirectoryName(asmPath)!;
        var outDir = Path.Combine(Path.GetTempPath(), "ma-idea-test-out");

        var ex = Assert.Throws<PackException>(() => Packer.Pack(new PackRequest
        {
            AssemblyPath = asmPath,
            OutputDir = outDir,
            ReferenceInputs = [asmDir],
        }));
        Assert.That(ex!.Message, Does.Contain("no entry type"));
    }

    [Test]
    public void ReconciledManifest_SerializesWireKernel()
    {
        // The shape the packer emits: manifestVersion + category(ContentKind) + kind(code) round-trips.
        var m = new IdeaManifest
        {
            ManifestVersion = 1, Category = "Plugin", Kind = "code", Key = "ui.tooltip", Version = 1,
            DisplayName = "Tooltip", Sdk = 1, EntryType = "IdeaCms.Plugin.Tooltip.V1",
        };
        var round = ManifestReader.Read(ManifestReader.Write(m));
        Assert.Multiple(() =>
        {
            Assert.That(round.ManifestVersion, Is.EqualTo(1));
            Assert.That(round.Category, Is.EqualTo("Plugin"));
            Assert.That(round.Kind, Is.EqualTo("code"));
            Assert.That(round.EntryType, Is.EqualTo("IdeaCms.Plugin.Tooltip.V1"));
        });
    }
}
