using System.Text.Json;

namespace IdeaCms.Packaging;

/// <summary>
/// Parses an <c>idea.json</c> string into an <see cref="IdeaManifest"/> using the one canonical
/// serializer. Unknown fields round-trip losslessly through <see cref="IdeaManifest.Extra"/>; only
/// malformed JSON throws (a <see cref="JsonException"/>). Validation of the parsed manifest is a
/// separate concern — see <see cref="ManifestValidator"/>.
/// </summary>
public static class ManifestReader
{
    public static IdeaManifest Read(string json) =>
        JsonSerializer.Deserialize<IdeaManifest>(json, IdeaManifest.ManifestJson)
        ?? throw new JsonException("idea.json deserialized to null.");

    public static IdeaManifest Read(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize<IdeaManifest>(utf8Json, IdeaManifest.ManifestJson)
        ?? throw new JsonException("idea.json deserialized to null.");

    /// <summary>Re-serialize a manifest with the canonical options (used to persist a normalized copy).</summary>
    public static string Write(IdeaManifest manifest) =>
        JsonSerializer.Serialize(manifest, IdeaManifest.ManifestJson);
}
