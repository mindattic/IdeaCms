using System.Security.Cryptography.X509Certificates;
using IdeaCms.Core.Services;

namespace IdeaCms.Tests.Packaging;

/// <summary>Trusts the shared <see cref="TestSigningFixture.PublicOnly"/> cert every test-built `.idea` is
/// signed with by default — the test double for <see cref="IPackageSigningTrust"/>. Pass a different
/// cert to simulate a host that trusts something other than what a package was actually signed with.</summary>
internal sealed class TestPackageSigningTrust(X509Certificate2? trustedCert = null) : IPackageSigningTrust
{
    public X509Certificate2 TrustedCertificate { get; } = trustedCert ?? TestSigningFixture.PublicOnly;
}
