using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Discovery;
using IdeaCms.Core.Rendering;
using IdeaCms.Packaging;
using IdeaCms.Tests.Packaging;

namespace IdeaCms.Tests.Discovery;

/// <summary>
/// The Test Harness's dependency resolver: read a folder of .idea files, validate + extract them, and emit
/// production-identical Origin=Package descriptors (with the manifest assets/uses on Extra) — so a page
/// previews against its real by-string dependencies with no database.
/// </summary>
[TestFixture]
public class LocalFolderPackageSourceTests
{
    private static void WriteIdea(string dir, string fileName, Stream bytes)
    {
        Directory.CreateDirectory(dir);
        using var fs = File.Create(Path.Combine(dir, fileName));
        bytes.CopyTo(fs);
    }

    [Test]
    public void Discover_ValidatesExtractsAndEmitsPackageDescriptors()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ma-deps-" + Guid.NewGuid().ToString("N"));
        var extractRoot = Path.Combine(Path.GetTempPath(), "ma-extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            // A valid component package with a head asset, plus an invalid one (host assembly in bin/).
            WriteIdea(folder, "tooltip.idea", IdeaTestArchive.Build(new Dictionary<string, string>
            {
                ["idea.json"] = ManifestReader.Write(new IdeaManifest
                {
                    ManifestVersion = 1, Category = "Plugin", Kind = "code", Key = "tooltip", Version = 1,
                    DisplayName = "Tooltip", Sdk = 1, EntryType = "IdeaCms.Plugin.Tooltip.V1",
                    AssemblyName = "Tooltip", Css = ["tooltip.css"], Scripts = ["tooltip.js"],
                }),
                ["bin/Tooltip.dll"] = "MZ-fake",
                ["wwwroot/tooltip.css"] = ".tt{}",
            }));
            WriteIdea(folder, "evil.idea", IdeaTestArchive.Build(new Dictionary<string, string>
            {
                ["idea.json"] = ManifestReader.Write(new IdeaManifest
                {
                    ManifestVersion = 1, Category = "Plugin", Kind = "code", Key = "evil", Version = 1,
                    DisplayName = "Evil", Sdk = 1, EntryType = "IdeaCms.Plugin.Evil.V1", AssemblyName = "Evil",
                }),
                ["bin/System.Text.Json.dll"] = "stowaway",   // host assembly -> validator rejects
            }));

            var extractor = new PackageExtractor(extractRoot);
            var descriptors = new LocalFolderPackageSource(folder, extractor).Discover().ToList();

            Assert.That(descriptors, Has.Count.EqualTo(1), "the invalid package is rejected, exactly as the host would");
            var d = descriptors[0];
            Assert.Multiple(() =>
            {
                Assert.That(d.Origin, Is.EqualTo(ContentOrigin.Package));
                Assert.That(d.Kind, Is.EqualTo(ContentKind.Plugin));
                Assert.That(d.Key, Is.EqualTo("tooltip"));
                Assert.That(d.AssetMount, Is.EqualTo("/_ideas/Plugin/tooltip/1"));
                Assert.That(extractor.IsExtracted("Plugin", "tooltip", 1, "Tooltip"), Is.True, "bin/ extracted for the ALC resolver");
            });

            // The manifest assets are surfaced onto Extra and mount under /_ideas exactly as in production.
            var assets = PageAssets.PackageAssetsOf(d);
            Assert.That(assets.Css, Is.EqualTo(new[] { "/_ideas/Plugin/tooltip/1/tooltip.css" }));
            Assert.That(assets.Scripts, Is.EqualTo(new[] { "/_ideas/Plugin/tooltip/1/tooltip.js" }));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            if (Directory.Exists(extractRoot)) Directory.Delete(extractRoot, recursive: true);
        }
    }
}
