using System.Security.Cryptography.X509Certificates;

namespace IdeaCms.Core.Services;

/// <summary>
/// The ONE trusted public certificate every install path verifies a `.idea`'s content signature
/// against (see <c>PackageSigner</c>). A pre-resolved collaborator, like every other dependency
/// <c>PackageInstallService</c> takes — never raw <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
/// </summary>
public interface IPackageSigningTrust
{
    X509Certificate2 TrustedCertificate { get; }
}
