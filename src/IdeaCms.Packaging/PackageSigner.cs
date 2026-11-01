using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdeaCms.Packaging;

/// <summary>
/// Signs and verifies a `.idea` archive's OWN content — independent of any transport (NuGet, a plain
/// file drop, an admin upload). Every install path funnels through <c>PackageInstallService.InstallAsync</c>,
/// so verifying here protects all of them uniformly, unlike relying on NuGet's own package-signing feature,
/// which would only ever cover packages that happened to arrive via a NuGet fetch.
/// <para>
/// The signature entry carries a signer THUMBPRINT only, never a full certificate: if the archive carried
/// its own signer cert, an attacker could embed a self-issued cert alongside a matching signature and it
/// would "verify" cleanly — trust would be reading itself out of untrusted bytes. The RSA public key used
/// to verify always comes from the host's own configured trusted certificate; the thumbprint in the
/// signature entry is a cheap diagnostic checked before any crypto runs, not the trust decision itself.
/// </para>
/// </summary>
public static class PackageSigner
{
    public const string SignatureEntryName = "idea.sig.json";
    public const string AlgorithmName = "RSA-SHA256-PSS";

    public enum VerifyOutcome
    {
        Ok,
        /// <summary>No idea.sig.json entry at all.</summary>
        NotSigned,
        /// <summary>idea.sig.json exists but could not be parsed, or is missing a required field.</summary>
        Malformed,
        /// <summary>The signature's claimed signer thumbprint does not match the host's trusted certificate.</summary>
        UntrustedSigner,
        /// <summary>The thumbprint matched, but the signature does not verify against the archive's content.</summary>
        BadSignature,
    }

    private sealed record SignaturePayload
    {
        [JsonPropertyName("algorithm")] public string Algorithm { get; init; } = AlgorithmName;
        [JsonPropertyName("signerThumbprint")] public string SignerThumbprint { get; init; } = "";
        [JsonPropertyName("signature")] public string Signature { get; init; } = "";
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// The exact bytes signed/verified: every entry's path + lowercase-hex SHA-256, ordinally sorted,
    /// tab-joined per line, newline-joined, UTF-8 no BOM — computed via <see cref="IdeaArchiveReader.HashAllEntries"/>
    /// so both sides canonicalize identically.
    /// </summary>
    public static byte[] CanonicalManifest(IdeaArchiveReader reader)
    {
        var sb = new StringBuilder();
        foreach (var (path, sha256Hex) in reader.HashAllEntries(excludeEntryName: SignatureEntryName))
            sb.Append(path).Append('\t').Append(sha256Hex).Append('\n');
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Signs an in-memory `.idea` zip in place: computes the canonical manifest over every OTHER entry,
    /// then writes/replaces <see cref="SignatureEntryName"/>. Idempotent — re-signing replaces any prior
    /// signature rather than stacking one.
    /// </summary>
    public static void Sign(MemoryStream ideaZipStream, X509Certificate2 signingCert)
    {
        byte[] canonical;
        ideaZipStream.Position = 0;
        using (var reader = IdeaArchiveReader.Open(ideaZipStream))
            canonical = CanonicalManifest(reader);

        using var rsa = signingCert.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("signing certificate has no RSA private key.");
        var signature = rsa.SignData(canonical, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);

        var json = JsonSerializer.Serialize(new SignaturePayload
        {
            Algorithm = AlgorithmName,
            SignerThumbprint = signingCert.Thumbprint,
            Signature = Convert.ToBase64String(signature),
        }, JsonOpts);

        ideaZipStream.Position = 0;
        using var zip = new ZipArchive(ideaZipStream, ZipArchiveMode.Update, leaveOpen: true);
        zip.GetEntry(SignatureEntryName)?.Delete();
        var entry = zip.CreateEntry(SignatureEntryName);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(json);
    }

    /// <summary>File-path convenience for the `ma-idea sign` CLI verb.</summary>
    public static void SignFile(string ideaPath, X509Certificate2 signingCert)
    {
        // Expandable stream: new MemoryStream(byte[]) is fixed-size, and adding the signature entry grows it.
        using var ms = new MemoryStream();
        ms.Write(File.ReadAllBytes(ideaPath));
        ms.Position = 0;
        Sign(ms, signingCert);
        File.WriteAllBytes(ideaPath, ms.ToArray());
    }

    /// <summary>
    /// Verifies <see cref="SignatureEntryName"/> against ONE trusted public certificate. Never throws for
    /// a missing/malformed/wrong signature — returns an outcome; the caller (the install path) decides
    /// how to react.
    /// </summary>
    public static VerifyOutcome Verify(IdeaArchiveReader reader, X509Certificate2 trustedCert)
    {
        var json = reader.ReadSignatureJson();
        if (json is null) return VerifyOutcome.NotSigned;

        SignaturePayload? payload;
        try { payload = JsonSerializer.Deserialize<SignaturePayload>(json, JsonOpts); }
        catch (JsonException) { return VerifyOutcome.Malformed; }

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.SignerThumbprint)
            || string.IsNullOrWhiteSpace(payload.Signature))
            return VerifyOutcome.Malformed;

        if (!string.Equals(payload.SignerThumbprint, trustedCert.Thumbprint, StringComparison.OrdinalIgnoreCase))
            return VerifyOutcome.UntrustedSigner;

        byte[] signature;
        try { signature = Convert.FromBase64String(payload.Signature); }
        catch (FormatException) { return VerifyOutcome.Malformed; }

        var canonical = CanonicalManifest(reader);
        using var rsa = trustedCert.GetRSAPublicKey()
            ?? throw new InvalidOperationException("trusted certificate has no RSA public key.");
        return rsa.VerifyData(canonical, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)
            ? VerifyOutcome.Ok
            : VerifyOutcome.BadSignature;
    }
}
