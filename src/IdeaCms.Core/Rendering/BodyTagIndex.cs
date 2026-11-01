using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Core.Rendering;

/// <summary>One citizen tag (<c>&lt;Component.X … /&gt;</c>, <c>&lt;Plugin.Y&gt;…&lt;/Plugin.Y&gt;</c>) found in a page body.</summary>
public sealed record BodyTag(
    int Index,
    int Start,
    int Length,
    string RawName,
    string Key,
    ContentKind? ExplicitKind,
    int? Version,
    IReadOnlyList<KeyValuePair<string, string?>> Attributes,
    bool SelfClosing,
    int Depth,
    int? ParentIndex)
{
    /// <summary>The tag's value for an attribute (case-insensitive), or null when absent/bare.</summary>
    public string? Get(string name) =>
        Attributes.FirstOrDefault(a => string.Equals(a.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}

/// <summary>
/// Indexes and rewrites citizen tags in a page's SOURCE html without re-serializing it (M.IDEA-§4.5): the tag
/// IS the component instance, so editing an instance's settings rewrites exactly that tag's attribute list
/// and leaves every other byte of the author's markup untouched. Uses the same PascalCase tag grammar as
/// <see cref="IncludeExpander"/>; tags inside comments, &lt;script&gt; and &lt;style&gt; are ignored.
/// </summary>
public static class BodyTagIndex
{
    private static readonly Regex Opening = new(
        @"<([A-Z][a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)*)((?:\s(?:[^>""']|""[^""]*""|'[^']*')*?)?)\s*(/)?>",
        RegexOptions.Compiled);
    private static readonly Regex Closing = new(@"</([A-Z][a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)*)\s*>", RegexOptions.Compiled);
    private static readonly Regex Opaque = new(
        @"<!--[\s\S]*?-->|<(script|style)\b[\s\S]*?</\1\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Attr = new(
        @"([^\s=/""'>]+)(?:\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+)))?", RegexOptions.Compiled);

    /// <summary>Attributes that address the citizen rather than configure it; never settings.</summary>
    public static readonly IReadOnlySet<string> MetaAttributes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "kind", "data-version", "data-key" };

    public static IReadOnlyList<BodyTag> Scan(string? html)
    {
        var tags = new List<BodyTag>();
        if (string.IsNullOrEmpty(html)) return tags;

        var opaque = Opaque.Matches(html).Select(m => (m.Index, End: m.Index + m.Length)).ToList();
        bool Hidden(int pos) => opaque.Any(r => pos >= r.Index && pos < r.End);

        var events = Opening.Matches(html).Select(m => (m.Index, Open: true, M: m))
            .Concat(Closing.Matches(html).Select(m => (m.Index, Open: false, M: m)))
            .Where(e => !Hidden(e.Index))
            .OrderBy(e => e.Index);

        var stack = new Stack<(string Name, int Index)>();
        foreach (var (_, open, m) in events)
        {
            var name = m.Groups[1].Value;
            if (!open)
            {
                if (stack.Any(s => s.Name == name))
                    while (stack.Count > 0 && stack.Pop().Name != name) { }
                continue;
            }

            var attrs = ParseAttributes(m.Groups[2].Value);
            var (key, kind) = ParseName(name);
            var kindAttr = attrs.FirstOrDefault(a => a.Key.Equals("kind", StringComparison.OrdinalIgnoreCase)).Value;
            if (kind is null && kindAttr is not null && Enum.TryParse<ContentKind>(kindAttr, true, out var k)) kind = k;
            int? version = int.TryParse(attrs.FirstOrDefault(a => a.Key.Equals("data-version", StringComparison.OrdinalIgnoreCase)).Value, out var v) ? v : null;
            var selfClosing = m.Groups[3].Success;

            var index = tags.Count;
            tags.Add(new BodyTag(index, m.Index, m.Length, name, key, kind, version, attrs, selfClosing,
                stack.Count, stack.Count > 0 ? stack.Peek().Index : null));
            if (!selfClosing) stack.Push((name, index));
        }
        return tags;
    }

    /// <summary>
    /// Returns <paramref name="html"/> with tag <paramref name="index"/>'s SETTING attributes replaced:
    /// each name in <paramref name="settingNames"/> takes its value from <paramref name="values"/> (null or
    /// absent = removed, i.e. back to the citizen's default); every other attribute is kept verbatim and in
    /// place. New settings are appended as camelCase attributes.
    /// </summary>
    public static string SetSettings(
        string html, int index, IEnumerable<string> settingNames, IReadOnlyDictionary<string, string?> values)
    {
        var tag = Scan(html).FirstOrDefault(t => t.Index == index)
                  ?? throw new ArgumentOutOfRangeException(nameof(index), "No such tag in the page body.");
        var names = new HashSet<string>(settingNames, StringComparer.OrdinalIgnoreCase);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = new List<KeyValuePair<string, string?>>();
        foreach (var a in tag.Attributes)
        {
            if (!names.Contains(a.Key)) { result.Add(a); continue; }
            if (!written.Add(a.Key)) continue;   // collapse duplicate spellings of one setting
            var nv = Lookup(values, a.Key);
            if (nv is not null) result.Add(new(a.Key, nv));
        }
        foreach (var n in names)
        {
            if (written.Contains(n)) continue;
            var nv = Lookup(values, n);
            if (nv is not null) result.Add(new(char.ToLowerInvariant(n[0]) + n[1..], nv));
        }

        var sb = new StringBuilder("<").Append(tag.RawName);
        foreach (var a in result)
        {
            sb.Append(' ').Append(a.Key);
            if (a.Value is not null) sb.Append("=\"").Append(Escape(a.Value)).Append('"');
        }
        sb.Append(tag.SelfClosing ? " />" : ">");
        return string.Concat(html.AsSpan(0, tag.Start), sb.ToString(), html.AsSpan(tag.Start + tag.Length));
    }

    private static string? Lookup(IReadOnlyDictionary<string, string?> values, string name)
    {
        foreach (var kv in values)
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    // < and > are escaped too: values are decoded on parse, and the tag pipeline (UpgradePascalCaseTags,
    // Scan) is regex-based, so a literal "<Component.X />" inside a re-emitted value would be upgraded.
    private static string Escape(string v) =>
        v.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static List<KeyValuePair<string, string?>> ParseAttributes(string tail)
    {
        var list = new List<KeyValuePair<string, string?>>();
        foreach (Match m in Attr.Matches(tail))
        {
            string? value = m.Groups[2].Success ? m.Groups[2].Value
                          : m.Groups[3].Success ? m.Groups[3].Value
                          : m.Groups[4].Success ? m.Groups[4].Value
                          : null;
            list.Add(new(m.Groups[1].Value, value is null ? null : WebUtility.HtmlDecode(value)));
        }
        return list;
    }

    private static (string Key, ContentKind? Kind) ParseName(string rawName)
    {
        var dot = rawName.IndexOf('.');
        if (dot > 0 && Enum.TryParse<ContentKind>(rawName[..dot], ignoreCase: true, out var kind))
            return (rawName[(dot + 1)..].ToLowerInvariant(), kind);
        return (rawName.ToLowerInvariant(), null);
    }
}
