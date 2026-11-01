using System.Text.RegularExpressions;
using AngleSharp.Css;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;

namespace IdeaCms.Core.Services;

/// <summary>
/// M.IDEA-§4.7: physically eliminates conflicting older CSS within a single author's own CSS text, rather
/// than leaving it for the browser's cascade to sort out. This is deliberately scoped to ONE CSS text at
/// a time -- it never reads or rewrites Theme/Component/Global CSS (those are either external files this
/// app doesn't own, or a host-wide setting shared by every page, so mutating them as a side effect of
/// saving one page would corrupt every other page's rendering). Precedence ACROSS tiers remains entirely
/// the job of cascade layers (M.IDEA-§4.7, CmsHead.razor); this class only cleans up a single tier's own
/// accumulated, possibly self-contradicting declarations.
///
/// The browser's cascade already resolves two separate "same selector" rule blocks correctly on its own
/// (same specificity/origin -> source order decides, per longhand property) -- so this isn't fixing a
/// rendering bug. Its value is readability/maintainability: a human editing the file sees ONE
/// authoritative block per selector with dead, superseded declarations physically removed, instead of
/// having to mentally simulate the cascade across scattered duplicate blocks.
///
/// Because that's the goal, this deliberately does a MINIMAL-DIFF rewrite: only the specific rule blocks
/// that are genuinely part of a same-selector duplicate group are touched. A hand-rolled, comment/string
/// -aware top-level tokenizer (<see cref="Segment"/>) finds those blocks by raw text span; everything
/// else -- comments, @media/@font-face/@keyframes blocks, single-occurrence rules, whitespace -- passes
/// through byte-for-byte untouched. (An earlier version ran the WHOLE text through AngleSharp.Css's
/// parse+reserialize round-trip; that's simpler but destroys every comment -- AngleSharp's CSSOM has no
/// concept of a comment at all -- and reformats every value it touches, e.g. "red" -> "rgba(255,0,0,1)",
/// even for rules with nothing to merge. Confirmed empirically against real library CSS files, several
/// of which have dozens of section comments; that trade-off was rejected.)
///
/// AngleSharp.Css is still used, narrowly, to compute the actual merged property set for a duplicate
/// group: its parser already expands every shorthand property into its true spec-defined longhand form
/// (margin/padding -> 4-way; border -> 12-way per-side width/style/color; border-radius -> 4 corners;
/// background -> its per-layer longhands; font, transition, animation, etc. likewise) -- confirmed
/// empirically; box-shadow/text-shadow are the one family it does NOT expand (no real longhand exists),
/// so they stay a single atomic value, matching the deliberate product decision never to invent a
/// non-standard decomposition for them.
/// </summary>
public static class CssConflictMerger
{
    private static readonly CssParser Parser = new();
    private static readonly DefaultDeclarationFactory DeclarationFactory = new();
    private static readonly Regex PropertyNamePattern = new(@"([a-zA-Z-]+)\s*:", RegexOptions.Compiled);

    /// <summary>
    /// Collapses every group of rules sharing an EXACT (trimmed, byte-identical) selector string into
    /// one rule at the position of its FIRST occurrence, applying real CSS "last non-losing declaration
    /// wins" semantics per longhand property (a later non-important declaration never overrides an
    /// earlier !important one; a later !important always wins over an earlier !important). Every later
    /// occurrence's rule text is dropped. Everything else -- comments, at-rules, single-occurrence
    /// rules, whitespace -- is returned exactly as written. On any failure this returns the original
    /// text unchanged rather than throwing (a page must never be invalid -- M.IDEA-LAW-7) or producing a
    /// half-merged, worse-than-the-input result.
    /// </summary>
    public static string Normalize(string? css)
    {
        if (string.IsNullOrWhiteSpace(css))
            return css ?? "";

        try
        {
            var segments = Tokenize(css);

            var candidates = segments
                .Where(s => s.SelectorText is not null)
                .GroupBy(s => s.SelectorText!, StringComparer.Ordinal)
                .Where(g => g.Count() > 1);

            var groups = new Dictionary<string, List<Segment>>(StringComparer.Ordinal);
            var mergedTextBySelector = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                // TryBuildMergedRuleText refuses to merge a group containing an unresolvable value (a
                // custom-property reference inside a shorthand -- see its doc comment); such groups are
                // simply left out of `groups` entirely, so every occurrence passes through untouched
                // below, exactly like any other non-duplicated selector.
                if (TryBuildMergedRuleText(candidate.Key, candidate.ToList(), out var mergedText))
                {
                    groups[candidate.Key] = candidate.ToList();
                    mergedTextBySelector[candidate.Key] = mergedText;
                }
            }

            if (groups.Count == 0)
                return css; // nothing safely mergeable -- the file is already exactly what it should be.

            var emitted = new HashSet<string>(StringComparer.Ordinal);

            var sb = new System.Text.StringBuilder(css.Length);
            foreach (var seg in segments)
            {
                if (seg.SelectorText is null || !groups.ContainsKey(seg.SelectorText))
                {
                    sb.Append(seg.RawText);
                    continue;
                }

                if (emitted.Add(seg.SelectorText))
                    sb.Append(mergedTextBySelector[seg.SelectorText]);
                // later occurrences of an already-emitted duplicate selector are dropped entirely.
            }

            return sb.ToString();
        }
        catch
        {
            return css;
        }
    }

    /// <summary>
    /// Builds the merged rule text for a duplicate-selector group, or refuses (returns false) if doing
    /// so would lose information. AngleSharp.Css's parser silently drops anything it can't fully resolve
    /// -- two distinct failure modes confirmed empirically against real library CSS: (1) a shorthand
    /// whose value is (or contains) a custom-property reference, e.g. <c>background: var(--bg)</c>,
    /// can't be resolved into concrete per-side/per-layer longhand values without knowing what
    /// <c>--bg</c> evaluates to (a full cascade/computed-style problem, out of scope here) -- every
    /// synthesized longhand slot comes back with an EMPTY value, and the shorthand's own name never
    /// appears in the enumeration at all; (2) a vendor-prefixed or otherwise unrecognized property, e.g.
    /// <c>-webkit-font-smoothing: antialiased</c>, is dropped from the parsed rule ENTIRELY, with no
    /// error and no trace. Rather than special-case each known failure mode, this does a general
    /// round-trip check: every property name the ORIGINAL text visibly declares (found via a plain regex
    /// over the raw text, independent of AngleSharp) must either appear literally among the properties
    /// AngleSharp gave back a real (non-empty) value for, or be a shorthand whose full real longhand set
    /// (per AngleSharp's own declaration factory) all did. Anything short of that aborts the merge for
    /// the WHOLE selector group -- every occurrence is left completely untouched, deferring to the
    /// browser's cascade exactly as an ordinary (non-duplicated) rule already does.
    /// </summary>
    private static bool TryBuildMergedRuleText(string selectorText, List<Segment> occurrences, out string mergedText)
    {
        var properties = new Dictionary<string, (string Value, bool Important)>(StringComparer.OrdinalIgnoreCase);

        foreach (var occurrence in occurrences)
        {
            var rule = (ICssStyleRule)Parser.ParseStyleSheet(occurrence.RawText).Rules[0];

            var resolvedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ICssProperty property in rule.Style)
            {
                if (string.IsNullOrEmpty(property.Value))
                    continue; // synthesized slot AngleSharp couldn't resolve -- not real coverage.
                resolvedNames.Add(property.Name);

                // A later, non-important declaration never displaces an earlier !important one.
                if (properties.TryGetValue(property.Name, out var existing) && existing.Important && !property.IsImportant)
                    continue;
                properties[property.Name] = (property.Value, property.IsImportant);
            }

            foreach (var declaredName in ExtractDeclaredPropertyNames(occurrence.RawText))
            {
                if (!IsCovered(declaredName, resolvedNames))
                {
                    mergedText = "";
                    return false;
                }
            }
        }

        var merged = (ICssStyleRule)Parser.ParseStyleSheet(selectorText + " {}").Rules[0];
        merged.Style.CssText = string.Join("; ", properties.Select(kv =>
            $"{kv.Key}: {kv.Value.Value}{(kv.Value.Important ? " !important" : "")}"));
        mergedText = merged.CssText;
        return true;
    }

    /// <summary>
    /// Some shorthands expand into further shorthands, not straight to leaf properties -- "border"'s own
    /// Longhands are border-width/border-style/border-color, each of which is ITSELF a shorthand for its
    /// 4 per-side leaves; AngleSharp's rule enumeration only ever surfaces the fully-flattened leaves, so
    /// covering "border" requires recursing through that chain rather than checking one level deep.
    /// </summary>
    private static bool IsCovered(string declaredName, HashSet<string> resolvedNames, int depth = 0)
    {
        if (resolvedNames.Contains(declaredName))
            return true;
        if (depth > 4)
            return false;
        var longhands = DeclarationFactory.Create(declaredName)?.Longhands ?? [];
        return longhands.Length > 0 && longhands.All(l => IsCovered(l, resolvedNames, depth + 1));
    }

    /// <summary>Property names an author literally wrote in a rule's body, independent of AngleSharp --
    /// the ground truth <see cref="IsCovered"/> checks AngleSharp's own output against.</summary>
    private static IEnumerable<string> ExtractDeclaredPropertyNames(string ruleRawText)
    {
        var bodyStart = ruleRawText.IndexOf('{');
        var bodyEnd = ruleRawText.LastIndexOf('}');
        if (bodyStart < 0 || bodyEnd <= bodyStart)
            yield break;

        foreach (Match m in PropertyNamePattern.Matches(ruleRawText[(bodyStart + 1)..bodyEnd]))
            yield return m.Groups[1].Value.Trim();
    }

    /// <summary>One top-level construct in the raw CSS text: a plain style rule (SelectorText set), or
    /// an opaque span (a comment, whitespace, an @-rule block, or anything else) carried through as-is.</summary>
    private readonly record struct Segment(string? SelectorText, string RawText);

    private static List<Segment> Tokenize(string css)
    {
        var segments = new List<Segment>();
        var n = css.Length;
        var i = 0;
        var lastEnd = 0;

        while (i < n)
        {
            var c = css[i];

            if (c == '/' && i + 1 < n && css[i + 1] == '*')
            {
                // A comment can never be part of a rule's prelude/selector -- flush everything since
                // lastEnd (any preceding whitespace plus the comment itself) as its own opaque segment,
                // so the NEXT rule's prelude starts cleanly right after it, never glued to comment text.
                var close = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var commentEnd = close < 0 ? n : close + 2;
                if (commentEnd > lastEnd)
                    segments.Add(new Segment(null, css[lastEnd..commentEnd]));
                i = commentEnd;
                lastEnd = i;
                continue;
            }
            if (c is '"' or '\'')
            {
                i = SkipString(css, i);
                continue;
            }
            if (c == '{')
            {
                var blockEnd = FindMatchingBrace(css, i);
                var prelude = css[lastEnd..i];
                var trimmedPrelude = prelude.Trim();
                var fullRaw = css[lastEnd..(blockEnd + 1)];

                var isPlainRule = trimmedPrelude.Length > 0 && trimmedPrelude[0] != '@';
                segments.Add(new Segment(isPlainRule ? trimmedPrelude : null, fullRaw));

                i = blockEnd + 1;
                lastEnd = i;
                continue;
            }
            i++;
        }

        if (lastEnd < n)
            segments.Add(new Segment(null, css[lastEnd..]));

        return segments;
    }

    /// <summary>Index just past the string literal starting at <paramref name="start"/> (which must be a quote).</summary>
    private static int SkipString(string css, int start)
    {
        var quote = css[start];
        var i = start + 1;
        while (i < css.Length)
        {
            if (css[i] == '\\') { i += 2; continue; }
            if (css[i] == quote) return i + 1;
            i++;
        }
        return css.Length;
    }

    /// <summary>Index of the '}' matching the '{' at <paramref name="openBrace"/>, honoring nested
    /// braces, comments and string literals inside the block (needed for @media/@keyframes bodies).</summary>
    private static int FindMatchingBrace(string css, int openBrace)
    {
        var depth = 0;
        var i = openBrace;
        while (i < css.Length)
        {
            var c = css[i];
            if (c == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                var close = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? css.Length : close + 2;
                continue;
            }
            if (c is '"' or '\'') { i = SkipString(css, i); continue; }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
            i++;
        }
        return css.Length - 1;
    }
}
