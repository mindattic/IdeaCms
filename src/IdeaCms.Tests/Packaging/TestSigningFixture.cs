using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace IdeaCms.Tests.Packaging;

/// <summary>
/// One shared ephemeral self-signed RSA cert for the whole test run, so every test fixture that builds
/// a `.idea` via <see cref="IdeaTestArchive"/> signs against the same key `PackageInstallServiceTests`'
/// <c>TestPackageSigningTrust</c> trusts — mirroring how a real deployment's Vault-kept cert is shared
/// across every install path.
/// </summary>
internal static class TestSigningFixture
{
    private static readonly Lazy<X509Certificate2> Full = new(CreateSelfSigned);
    private static readonly Lazy<X509Certificate2> PublicOnlyValue = new(() =>
        X509CertificateLoader.LoadCertificate(Full.Value.Export(X509ContentType.Cert)));

    /// <summary>Full cert including the RSA private key — used to sign.</summary>
    public static X509Certificate2 SigningCert => Full.Value;

    /// <summary>Public-key-only cert — what a verifier (mirroring <c>IPackageSigningTrust</c>) trusts.</summary>
    public static X509Certificate2 PublicOnly => PublicOnlyValue.Value;

    /// <summary>A different, untrusted cert — for negative "wrong signer" tests.</summary>
    public static X509Certificate2 CreateUntrusted() => CreateSelfSigned();

    private static X509Certificate2 CreateSelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=IdeaCms Test Signing", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
    }
}
