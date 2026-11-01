using System.IO.Compression;
using System.Text;
using IdeaCms.Packaging;

namespace IdeaCms.Tests.Packaging;

[TestFixture]
public class PackageSignerTests
{
    private static MemoryStream BuildUnsigned() =>
        IdeaTestArchive.Build(new Dictionary<string, string>
        {
            ["idea.json"] = ManifestReader.Write(new IdeaManifest
            {
                ManifestVersion = 1, Category = "Plugin", Kind = "code", Key = "ui.tooltip", Version = 1,
                DisplayName = "Tooltip", Sdk = 1, EntryType = "IdeaCms.Plugin.Demo.V1", AssemblyName = "Demo",
            }),
            ["bin/Demo.dll"] = "MZ-fake",
        }, sign: false);

    [Test]
    public void RoundTrip_SignThenVerify_IsOk()
    {
        var archive = BuildUnsigned();
        PackageSigner.Sign(archive, TestSigningFixture.SigningCert);
        archive.Position = 0;

        using var reader = IdeaArchiveReader.Open(archive);
        var outcome = PackageSigner.Verify(reader, TestSigningFixture.PublicOnly);

        Assert.That(outcome, Is.EqualTo(PackageSigner.VerifyOutcome.Ok));
    }

    [Test]
    public void SignFile_OnARealFile_SignsInPlace()
    {
        // Regression: SignFile wrapped the bytes in a fixed-size MemoryStream, so `ma-idea sign` threw
        // "Memory stream is not expandable" on every real package.
        var path = Path.Combine(Path.GetTempPath(), $"signfile-{Guid.NewGuid():N}.idea");
        try
        {
            File.WriteAllBytes(path, BuildUnsigned().ToArray());
            PackageSigner.SignFile(path, TestSigningFixture.SigningCert);

            using var fs = File.OpenRead(path);
            using var reader = IdeaArchiveReader.Open(fs);
            Assert.That(PackageSigner.Verify(reader, TestSigningFixture.PublicOnly), Is.EqualTo(PackageSigner.VerifyOutcome.Ok));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void MissingSignature_IsNotSigned()
    {
        var archive = BuildUnsigned();
        using var reader = IdeaArchiveReader.Open(archive);

        Assert.That(PackageSigner.Verify(reader, TestSigningFixture.PublicOnly), Is.EqualTo(PackageSigner.VerifyOutcome.NotSigned));
    }

    [Test]
    public void TamperedContent_AfterSigning_IsBadSignature()
    {
        var archive = BuildUnsigned();
        PackageSigner.Sign(archive, TestSigningFixture.SigningCert);
        var tampered = IdeaTestArchive.Tamper(archive, "bin/Demo.dll", "TAMPERED");

        using var reader = IdeaArchiveReader.Open(tampered);
        Assert.That(PackageSigner.Verify(reader, TestSigningFixture.PublicOnly), Is.EqualTo(PackageSigner.VerifyOutcome.BadSignature));
    }

    [Test]
    public void WrongTrustedCertificate_IsUntrustedSigner()
    {
        var archive = BuildUnsigned();
        PackageSigner.Sign(archive, TestSigningFixture.SigningCert);
        archive.Position = 0;

        using var reader = IdeaArchiveReader.Open(archive);
        var wrongCert = TestSigningFixture.CreateUntrusted();
        Assert.That(PackageSigner.Verify(reader, wrongCert), Is.EqualTo(PackageSigner.VerifyOutcome.UntrustedSigner));
    }

    [Test]
    public void MalformedSignatureJson_IsMalformed()
    {
        var archive = BuildUnsigned();
        archive.Position = 0;
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.CreateEntry(PackageSigner.SignatureEntryName);
            using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            w.Write("{ not valid json");
        }
        archive.Position = 0;

        using var reader = IdeaArchiveReader.Open(archive);
        Assert.That(PackageSigner.Verify(reader, TestSigningFixture.PublicOnly), Is.EqualTo(PackageSigner.VerifyOutcome.Malformed));
    }

    [Test]
    public void ReSigning_IsIdempotent_ReplacesPriorSignature()
    {
        var archive = BuildUnsigned();
        PackageSigner.Sign(archive, TestSigningFixture.SigningCert);
        PackageSigner.Sign(archive, TestSigningFixture.SigningCert);   // sign again
        archive.Position = 0;

        using var reader = IdeaArchiveReader.Open(archive);
        Assert.That(PackageSigner.Verify(reader, TestSigningFixture.PublicOnly), Is.EqualTo(PackageSigner.VerifyOutcome.Ok));
    }
}
