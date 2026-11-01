using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using IdeaCms.Core.Secrets;

namespace IdeaCms.Tests;

[TestFixture]
public class VaultPackageSigningTrustTests
{
    private static (string B64, string Thumbprint) NewCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=test-signing", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return (Convert.ToBase64String(cert.Export(X509ContentType.Cert)), cert.Thumbprint);
    }

    private static VaultPackageSigningTrust Trust(string key, string value) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build());

    [Test]
    public void ReadsTheCanonicalKey()
    {
        var (b64, thumb) = NewCert();
        var trust = Trust("MindAttic:Vault:PackageSigning:signing-cert-public", b64);
        Assert.That(trust.TrustedCertificate.Thumbprint, Is.EqualTo(thumb));
    }

    [TestCase("MindAttic:Vault:PackageSigning:signingcertpublic")]
    [TestCase("MindAttic:Vault:PackageSigning:signing_cert_public")]
    public void ReadsTheNameAppServiceOnLinuxRewritesItTo(string key)
    {
        var (b64, thumb) = NewCert();
        Assert.That(Trust(key, b64).TrustedCertificate.Thumbprint, Is.EqualTo(thumb),
            "App Service on Linux drops hyphens from setting names; the trust must still find the cert");
    }

    [Test]
    public void MissingCertFailsClosedWithTheCanonicalKeyInTheMessage()
    {
        var trust = Trust("MindAttic:Vault:PackageSigning:something-else", "x");
        var ex = Assert.Throws<InvalidOperationException>(() => _ = trust.TrustedCertificate);
        Assert.That(ex!.Message, Does.Contain("signing-cert-public"));
    }
}
