namespace IdeaCms.Core.Entities;

/// <summary>
/// A tenant/portal resolved by host header. Multi-site is a seam from day one: Pages carry a nullable
/// SiteId, so running single-site today and multi-tenant later is a resolver change, not a schema break.
/// </summary>
public sealed class Site : ContentEntityBase
{
    public string Key { get; set; } = "";          // upsert authority, e.g. "default"
    public string Name { get; set; } = "";
    /// <summary>Comma/space separated host bindings, e.g. "mindattic.com,www.mindattic.com".</summary>
    public string HostBindings { get; set; } = "";
    public string DefaultThemeKey { get; set; } = "";
    public int DefaultThemeVersion { get; set; } = 1;
    /// <summary>"light" or "dark" — the mode a first-time visitor sees before any ThemeToggle choice.</summary>
    public string DefaultThemeMode { get; set; } = "light";
    public bool IsDefault { get; set; }
    public string? SettingsJson { get; set; }
}
