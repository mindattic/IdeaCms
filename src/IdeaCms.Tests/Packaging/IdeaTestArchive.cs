using System.IO.Compression;
using System.Text;
using IdeaCms.Packaging;

namespace IdeaCms.Tests.Packaging;

/// <summary>Synthesizes a <c>.idea</c> archive in memory so the read path can be tested without disk.</summary>
internal static class IdeaTestArchive
{
    /// <summary>
    /// Build a zip with the given entry paths→content. Returns a seekable stream positioned at 0.
    /// Signed by default with <see cref="TestSigningFixture.SigningCert"/> — the same cert
    /// <see cref="TestPackageSigningTrust"/> trusts — so every existing test that builds a fixture
    /// through this helper needs no per-test change; pass <paramref name="sign"/>=false only for a
    /// test that specifically exercises the unsigned/tampered path.
    /// </summary>
    public static MemoryStream Build(IReadOnlyDictionary<string, string> entries, bool sign = true)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var e = zip.CreateEntry(path);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(content);
            }
        }
        if (sign) PackageSigner.Sign(ms, TestSigningFixture.SigningCert);
        ms.Position = 0;
        return ms;
    }

    /// <summary>A minimal valid code-package manifest JSON with one bin/ assembly.</summary>
    public static MemoryStream CodePackage(string key = "ui.tooltip", int version = 1, string category = "Plugin", bool sign = true) =>
        Build(new Dictionary<string, string>
        {
            ["idea.json"] = ManifestReader.Write(new IdeaManifest
            {
                ManifestVersion = 1, Category = category, Kind = "code", Key = key, Version = version,
                DisplayName = "Tooltip", Sdk = 1, EntryType = $"IdeaCms.{category}.Demo.V{version}",
                AssemblyName = "Demo", Assets = ["css/x.css"],
            }),
            ["bin/Demo.dll"] = "MZ-fake",
            ["wwwroot/css/x.css"] = ".x{}",
        }, sign);

    /// <summary>
    /// Rewrites one entry's content in an already-built (and possibly signed) archive, WITHOUT
    /// re-signing — for tests that need a genuinely tampered package (signature present, content changed).
    /// </summary>
    public static MemoryStream Tamper(MemoryStream archive, string entryName, string newContent)
    {
        archive.Position = 0;
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Update, leaveOpen: true))
        {
            zip.GetEntry(entryName)?.Delete();
            var entry = zip.CreateEntry(entryName);
            using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            w.Write(newContent);
        }
        archive.Position = 0;
        return archive;
    }
}
