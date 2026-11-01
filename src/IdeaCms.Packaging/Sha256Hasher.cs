using System.Security.Cryptography;

namespace IdeaCms.Packaging;

/// <summary>Deterministic lowercase-hex SHA-256 of package bytes — the install-time integrity stamp.</summary>
public static class Sha256Hasher
{
    public static string OfBytes(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string OfStream(Stream stream) =>
        Convert.ToHexStringLower(SHA256.HashData(stream));
}
