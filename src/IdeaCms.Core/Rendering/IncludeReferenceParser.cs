using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using IdeaCms.Abstractions;

namespace IdeaCms.Core.Rendering;

/// <summary>
/// Parses include references from author HTML. The ONLY supported form is the PascalCase HTML
/// component tag: <c>&lt;Alert /&gt;</c>, <c>&lt;Alert kind="Plugin" /&gt;</c>,
/// <c>&lt;Alert&gt;…&lt;/Alert&gt;</c> (nested). Tags are pre-processed by
/// <see cref="IncludeExpander.UpgradePascalCaseTags"/> into <c>&lt;ma-component data-key="…"&gt;</c>
/// before AngleSharp parses so that tag names, attributes, and children are handled correctly.
/// Extracted so the renderer (<see cref="IncludeExpander"/>) and the delete-guard
/// (ContentLifecycleService) parse references identically — no divergent duplicate.
/// The kind segment is REQUIRED (the catalog/guard/hoist key on kind+key).
/// </summary>
public static class IncludeReferenceParser
{
    private static readonly HtmlParser Parser = new();

    /// <summary>Every include reference in author HTML, in document order. Handles PascalCase HTML
    /// component tags (<c>&lt;Alert /&gt;</c>, <c>&lt;Alert kind="Plugin"&gt;…&lt;/Alert&gt;</c>).
    /// Brace tokens (<c>{{ … }}</c>) are no longer part of the grammar; <see cref="SeedService"/>
    /// migrates any remaining old-format bodies at startup so the guard stays complete.
    /// Empty for null/blank.</summary>
    public static IReadOnlyList<(ContentKind Kind, string Key, int? Version)> Parse(string? html)
    {
        var result = new List<(ContentKind, string, int?)>();
        if (string.IsNullOrWhiteSpace(html)) return result;
        // Upgrade PascalCase tags so Walk finds them as ma-component elements rather than unknown text.
        var upgraded = IncludeExpander.UpgradePascalCaseTags(html);
        using var doc = Parser.ParseDocument("<!DOCTYPE html><html><head></head><body>" + upgraded + "</body></html>");
        Walk(doc.Body!.ChildNodes, result);
        return result;
    }

    private static void Walk(INodeList nodes, List<(ContentKind, string, int?)> acc)
    {
        foreach (var node in nodes)
        {
            if (node is IElement el)
            {
                // PascalCase HTML tag form — UpgradePascalCaseTags rewrites these to ma-component before Walk.
                if (el.LocalName == "ma-component")
                {
                    var key = el.GetAttribute("data-key") ?? "";
                    if (key.Length > 0)
                    {
                        var kindStr = el.GetAttribute("kind");
                        ContentKind kind = ContentKind.Component; // default; Plugin is a fallback at render time
                        // Only an attribute that PARSES overrides the default. Enum.TryParse writes
                        // default(ContentKind) — i.e. Page — into its out param when it fails, so an
                        // unrecognised kind (a typo, or a retired name like "Widget") used to be recorded
                        // as a Page reference: the delete guard then protected the wrong citizen, while
                        // IncludeExpander ignored the bad attribute and resolved Component-then-Plugin.
                        if (kindStr is not null
                            && Enum.TryParse<ContentKind>(kindStr, ignoreCase: true, out var parsedKind))
                            kind = parsedKind;
                        int? version = null;
                        if (int.TryParse(el.GetAttribute("data-version"), out var v)) version = v;
                        acc.Add((kind, key, version));
                    }
                }
                Walk(el.ChildNodes, acc);
            }
        }
    }

    /// <summary>
    /// Parse "[ideacms.|mindattic.ideas.]{kind}.{key...}[.v{n}|.latest]". The qualifying prefix is
    /// OPTIONAL — <c>theme.cyberspace</c>, <c>ideacms.theme.cyberspace</c> and the pre-rename
    /// <c>mindattic.ideas.theme.cyberspace</c> all parse identically, so content authored before the
    /// MindAttic.Ideas -> IdeaCms rename keeps resolving. Version null = float to latest.
    /// </summary>
    public static bool TryParseTag(string localName, out ContentKind kind, out string key, out int? version)
    {
        kind = default; key = ""; version = null;
        if (string.IsNullOrWhiteSpace(localName)) return false;

        // The qualifying prefix is optional; strip it if present, then parse {kind}.{key}[.v{n}|.latest].
        var s = localName.Trim();
        if (s.StartsWith("ideacms.", StringComparison.OrdinalIgnoreCase))
            s = s["ideacms.".Length..];
        else if (s.StartsWith("mindattic.ideas.", StringComparison.OrdinalIgnoreCase))
            s = s["mindattic.ideas.".Length..];

        var parts = s.Split('.');
        if (parts.Length < 2 || !Enum.TryParse(parts[0], ignoreCase: true, out kind)) return false;

        int keyEnd = parts.Length;
        if (TryParseVersion(parts[^1], out var v)) { version = v; keyEnd = parts.Length - 1; }
        else if (parts[^1].Equals("latest", StringComparison.OrdinalIgnoreCase)) { keyEnd = parts.Length - 1; }

        key = string.Join('.', parts[1..keyEnd]);
        return key.Length > 0;
    }

    public static bool TryParseVersion(string seg, out int version)
    {
        version = 0;
        return seg.Length > 1 && seg[0] is 'v' or 'V' && int.TryParse(seg.AsSpan(1), out version);
    }

    /// <summary>True if author HTML pins this exact (kind,key,version).</summary>
    public static bool BodyPinsVersion(string? html, ContentKind kind, string key, int version) =>
        Parse(html).Any(r => r.Kind == kind && string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase) && r.Version == version);

    /// <summary>True if author HTML references this key at all (pinned or floating).</summary>
    public static bool BodyReferencesKey(string? html, ContentKind kind, string key) =>
        Parse(html).Any(r => r.Kind == kind && string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>True if author HTML references this key with NO version pin (floats to latest).</summary>
    public static bool BodyFloatsKey(string? html, ContentKind kind, string key) =>
        Parse(html).Any(r => r.Kind == kind && string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase) && r.Version is null);

    // ---- uses[] manifest grammar: "<Kind>.<key>[@<version>]" (the declarative dependency form a
    //      COMPILED page emits via [Uses], parallel to the data-page include tag). ----

    /// <summary>Parse one manifest <c>uses[]</c> entry, e.g. "Component.tooltip" or "Theme.cyberspace@1".</summary>
    public static bool TryParseUse(string? entry, out ContentKind kind, out string key, out int? version)
    {
        kind = default; key = ""; version = null;
        if (string.IsNullOrWhiteSpace(entry)) return false;
        var s = entry.Trim();

        var at = s.IndexOf('@');
        if (at >= 0)
        {
            if (int.TryParse(s.AsSpan(at + 1), out var v)) version = v;
            s = s[..at];
        }

        var dot = s.IndexOf('.');
        if (dot <= 0 || dot >= s.Length - 1) return false;
        if (!Enum.TryParse(s[..dot], ignoreCase: true, out kind)) return false;
        key = s[(dot + 1)..].Trim().ToLowerInvariant();
        return key.Length > 0;
    }

    /// <summary>Every parseable <c>uses[]</c> reference, in declared order. Empty for null/blank entries.</summary>
    public static IReadOnlyList<(ContentKind Kind, string Key, int? Version)> ParseUses(IEnumerable<string>? uses)
    {
        var result = new List<(ContentKind, string, int?)>();
        if (uses is null) return result;
        foreach (var u in uses)
            if (TryParseUse(u, out var k, out var key, out var v)) result.Add((k, key, v));
        return result;
    }

    /// <summary>True if a <c>uses[]</c> list pins this exact (kind,key,version).</summary>
    public static bool UsesPinsVersion(IEnumerable<string>? uses, ContentKind kind, string key, int version) =>
        ParseUses(uses).Any(r => r.Kind == kind && string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase) && r.Version == version);

    /// <summary>True if a <c>uses[]</c> list references this key with NO version pin (floats to latest).</summary>
    public static bool UsesFloatsKey(IEnumerable<string>? uses, ContentKind kind, string key) =>
        ParseUses(uses).Any(r => r.Kind == kind && string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase) && r.Version is null);
}
