using System.Text.RegularExpressions;
using AngleSharp.Css;

namespace IdeaCms.Core.Services;

/// <summary>One shorthand property found alongside some, but not all, of its longhand siblings.</summary>
public sealed record CssLintWarning(string Shorthand, IReadOnlyList<string> CoOccurringLonghands, string Message);

/// <summary>
/// Advisory (never blocking) scan of author-typed PageCss for a specific footgun: cascade layers
/// guarantee which TIER wins (Component beats Page, M.IDEA-§4.7), but WITHIN a tier a shorthand
/// declaration (e.g. "margin: 10px") still overwrites all of its longhand sub-properties in source
/// order, same as always. An author who writes both a shorthand and one (but not all) of its longhand
/// siblings anywhere in PageCss most likely meant a single-side override that the shorthand will
/// silently clobber. This is a pure text heuristic, not a CSS parser: it is whole-document scoped (not
/// selector-block scoped) and blind to Theme/Global CSS, so it can flag pairs that don't actually target
/// the same element, and can't see cross-tier collisions -- acceptable for an advisory hint.
///
/// <see cref="CssConflictMerger"/> (M.IDEA-§4.7) automatically expands and collapses same-selector
/// shorthand/longhand conflicts within Untrusted PageCss at save time, using AngleSharp.Css's real
/// parser rather than this regex heuristic. This linter's remaining job is everything the merger
/// doesn't cover: Author-trusted PageCss (the merger never touches it -- M.IDEA-LAW-5 verbatim guarantee),
/// and non-exact-selector conflicts the merger deliberately leaves alone.
/// </summary>
public static class CssShorthandLinter
{
    /// <summary>
    /// Shorthand -> real longhand siblings, sourced from AngleSharp.Css's own declaration factory (the
    /// same authority <see cref="CssConflictMerger"/> parses with) rather than a hand-maintained table,
    /// so the two can't drift. box-shadow/text-shadow are deliberately absent -- CSS defines no real
    /// longhand for either (confirmed: the factory returns an empty Longhands list for both), so they
    /// stay a single atomic value everywhere in this codebase, never force-decomposed.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> ShorthandLonghands = BuildShorthandTable();

    private static Dictionary<string, string[]> BuildShorthandTable()
    {
        var factory = new DefaultDeclarationFactory();
        var candidates = new[]
        {
            "margin", "padding", "border", "border-radius", "outline", "background", "font",
            "list-style", "gap", "flex", "inset", "text-decoration", "place-items", "place-content",
            "place-self", "transition", "animation", "box-shadow", "text-shadow",
        };

        var table = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in candidates)
        {
            var longhands = factory.Create(name)?.Longhands ?? [];
            if (longhands.Length > 0)
                table[name] = longhands;
        }
        return table;
    }

    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex StringLiteral = new("\"(?:[^\"\\\\]|\\\\.)*\"|'(?:[^'\\\\]|\\\\.)*'", RegexOptions.Compiled);
    private static readonly Regex Declaration = new(@"([a-zA-Z-]+)\s*:\s*[^;{}]+;", RegexOptions.Compiled);

    public static IReadOnlyList<CssLintWarning> Scan(string? css)
    {
        if (string.IsNullOrWhiteSpace(css))
            return [];

        var stripped = StringLiteral.Replace(BlockComment.Replace(css, string.Empty), string.Empty);

        var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Declaration.Matches(stripped))
            properties.Add(m.Groups[1].Value.Trim());

        var warnings = new List<CssLintWarning>();
        foreach (var (shorthand, longhands) in ShorthandLonghands)
        {
            if (!properties.Contains(shorthand))
                continue;

            var present = longhands.Where(properties.Contains).ToArray();
            if (present.Length == 0 || present.Length == longhands.Length)
                continue;

            warnings.Add(new CssLintWarning(shorthand, present,
                $"PageCss uses shorthand \"{shorthand}\" together with longhand {string.Join(", ", present)}. " +
                $"Cascade layers only decide which tier wins — within the Page layer itself, \"{shorthand}\" still " +
                $"overwrites {string.Join(", ", present)} in source order. If you meant to change only one part, " +
                $"use the longhand properties instead of \"{shorthand}\"."));
        }

        return warnings;
    }
}
