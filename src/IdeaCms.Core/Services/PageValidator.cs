using System.Globalization;
using IdeaCms.Abstractions;
using IdeaCms.Core.Rendering;

namespace IdeaCms.Core.Services;

public enum PageIssueSeverity { Warning, Error }

/// <summary>One finding about a page body (M.IDEA-§4.6).</summary>
public sealed record PageIssue(PageIssueSeverity Severity, string Message);

/// <summary>
/// A citizen's declared settings for validation: name → value kind ("bool" | "integer" | "number" | "enum" |
/// "string"). Null = the citizen is not installed. Save time fills this from the loaded types; the test
/// suite fills it from package manifests, so both run the SAME rules.
/// </summary>
public delegate IReadOnlyDictionary<string, string>? CitizenSchemaLookup(ContentKind kind, string key, int? version);

/// <summary>
/// Validates a page body (M.IDEA-§4.6): every citizen tag resolves, its attributes are declared settings with
/// values of the right type, and nothing in the markup is silently removed by the XSS sanitizer.
/// </summary>
public static class PageMarkupValidator
{
    // Attributes a tag may always carry without being a declared setting.
    private static bool IsPassThrough(string name) =>
        BodyTagIndex.MetaAttributes.Contains(name)
        || name is "class" or "id" or "style" or "title" or "role"
        || name.StartsWith("data-", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("aria-", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<PageIssue> Validate(
        string? bodyHtml, ContentTrust trust, CitizenSchemaLookup lookup, IRawContentGate? gate = null)
    {
        var issues = new List<PageIssue>();
        if (string.IsNullOrWhiteSpace(bodyHtml)) return issues;

        var tags = BodyTagIndex.Scan(bodyHtml);
        if (trust != ContentTrust.Author && tags.Count > 0)
            issues.Add(new(PageIssueSeverity.Warning,
                $"This page is Untrusted, so its {tags.Count} component tag(s) will not render (only Author pages compose citizens)."));

        foreach (var tag in tags)
        {
            var (kind, schema) = Resolve(tag, lookup);
            var label = $"<{tag.RawName}>";
            if (schema is null)
            {
                issues.Add(new(PageIssueSeverity.Error, $"{label} is not an installed Component or Plugin — it will render as a placeholder."));
                continue;
            }
            foreach (var (name, value) in tag.Attributes)
            {
                if (IsPassThrough(name)) continue;
                var declared = schema.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
                if (declared is null)
                {
                    issues.Add(new(PageIssueSeverity.Warning, $"{label}: '{name}' is not a setting of {kind}.{tag.Key}; it passes through untyped."));
                    continue;
                }
                if (!ValueFits(schema[declared], value))
                    issues.Add(new(PageIssueSeverity.Error, $"{label}: '{name}=\"{value}\"' is not a valid {schema[declared]}."));
            }
        }

        if (gate is not null)
        {
            var html = trust == ContentTrust.Author ? IncludeExpander.UpgradePascalCaseTags(bodyHtml) : bodyHtml;
            foreach (var removed in gate.Audit(html, trust))
                issues.Add(new(PageIssueSeverity.Warning, $"Removed by the XSS sanitizer: {removed}."));
        }
        return issues;
    }

    private static (ContentKind Kind, IReadOnlyDictionary<string, string>? Schema) Resolve(BodyTag tag, CitizenSchemaLookup lookup)
    {
        if (tag.ExplicitKind is { } k) return (k, lookup(k, tag.Key, tag.Version));
        var c = lookup(ContentKind.Component, tag.Key, tag.Version);
        if (c is not null) return (ContentKind.Component, c);
        return (ContentKind.Plugin, lookup(ContentKind.Plugin, tag.Key, tag.Version));
    }

    private static bool ValueFits(string kind, string? value) => kind switch
    {
        "bool" => value is null || bool.TryParse(value, out _),               // a bare attribute means true
        "integer" => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        "number" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
        _ => true,                                                             // enum names are checked at bind
    };

    /// <summary>A schema lookup over the host's live catalog (loaded types).</summary>
    public static CitizenSchemaLookup FromCatalog(IContentCatalog catalog) => (kind, key, version) =>
    {
        var res = catalog.ResolveTag(kind, key, version);
        if (res.Outcome != ContentResolution.Resolved || res.Type is null) return null;
        return SettingsSchema.For(res.Type).ToDictionary(
            d => d.Name,
            d => d.ValueKind switch
            {
                SettingValueKind.Bool => "bool",
                SettingValueKind.Integer => "integer",
                SettingValueKind.Number => "number",
                SettingValueKind.Choice => "enum",
                _ => "string",
            },
            StringComparer.OrdinalIgnoreCase);
    };
}

/// <summary>Save-time page validation against the live catalog and the real sanitizer.</summary>
public interface IPageValidator
{
    IReadOnlyList<PageIssue> Validate(string? bodyHtml, ContentTrust trust);
}

public sealed class PageValidator(IContentCatalog catalog, IRawContentGate gate) : IPageValidator
{
    public IReadOnlyList<PageIssue> Validate(string? bodyHtml, ContentTrust trust) =>
        PageMarkupValidator.Validate(bodyHtml, trust, PageMarkupValidator.FromCatalog(catalog), gate);
}
