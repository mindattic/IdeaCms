using System.Text.Json.Serialization;

namespace IdeaCms.Packaging;

/// <summary>
/// The optional <c>data/page.json</c> a Page (code) package carries to make itself ROUTABLE on install
/// (the DNN/Orchard "recipe on install" pattern). The host applies it idempotently by (SiteId, Slug). The
/// render kind and CLR entry type come from the manifest — NOT from here; this only carries where the page
/// lives and which theme it wears. Every field is optional except <see cref="Slug"/>.
/// </summary>
public sealed record PageSeed
{
    /// <summary>The route this page is served at (unique per site). Required.</summary>
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";

    [JsonPropertyName("title")] public string Title { get; init; } = "";

    /// <summary>Theme this page wears, referenced BY STRING (decoupled from the package). Null = site default.</summary>
    [JsonPropertyName("themeKey")] public string? ThemeKey { get; init; }
    /// <summary>Pinned theme version; null = float to latest / site default.</summary>
    [JsonPropertyName("themeVersion")] public int? ThemeVersion { get; init; }

    /// <summary>Whether the page is published on install. Default true.</summary>
    [JsonPropertyName("published")] public bool Published { get; init; } = true;

    /// <summary>Optional target site by its <c>Key</c>; null = the default site.</summary>
    [JsonPropertyName("siteKey")] public string? SiteKey { get; init; }
}
