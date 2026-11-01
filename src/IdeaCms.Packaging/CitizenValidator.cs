using System.Text.RegularExpressions;

namespace IdeaCms.Packaging;

/// <summary>
/// Build/pack-time safety gate for a library citizen (M.IDEA-§4.6). The packer refuses to produce a package
/// that fails it, and the test suite runs the same checks over every shipped package, so an unsafe or
/// malformed citizen can never reach a site. Deliberately a short list of patterns with no legitimate use
/// in a citizen asset — not a general linter.
/// </summary>
public static partial class CitizenValidator
{
    [GeneratedRegex(@"\beval\s*\(|\bnew\s+Function\s*\(|\bdocument\.write(?:ln)?\s*\(|\bset(?:Timeout|Interval)\s*\(\s*['""`]")]
    private static partial Regex UnsafeScript();

    [GeneratedRegex(@"expression\s*\(|javascript\s*:|-moz-binding|(?<![-\w])behavior\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex UnsafeCss();

    /// <summary>Attribute names the include grammar consumes itself; a setting by these names would never bind.</summary>
    private static readonly HashSet<string> ReservedSettingNames =
        new(StringComparer.OrdinalIgnoreCase) { "kind", "data-key", "data-version" };

    /// <summary>Unsafe patterns in the citizen's own JS/CSS assets, as "path: problem" messages.</summary>
    public static IReadOnlyList<string> ValidateAssets(IEnumerable<(string Path, string Content)> assets)
    {
        var problems = new List<string>();
        foreach (var (path, content) in assets)
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            var rx = ext switch { ".js" or ".mjs" => UnsafeScript(), ".css" => UnsafeCss(), _ => null };
            if (rx is null) continue;
            foreach (Match m in rx.Matches(StripComments(content, ext)))
                problems.Add($"{path}: forbidden pattern '{m.Value.Trim()}'");
        }
        return problems;
    }

    /// <summary>Malformed instance-settings declarations (M.IDEA-§4.5), as messages.</summary>
    public static IReadOnlyList<string> ValidateSettings(IEnumerable<IdeaManifestSetting> settings)
    {
        var problems = new List<string>();
        foreach (var g in settings.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            problems.Add($"settings '{string.Join("', '", g.Select(s => s.Name))}' differ only by case — tag attributes are case-insensitive, so they collide");
        foreach (var s in settings.Where(s => ReservedSettingNames.Contains(s.Name)))
            problems.Add($"setting '{s.Name}' uses a name the include grammar reserves; it would never bind");
        return problems;
    }

    // Patterns inside comments are documentation, not code.
    private static string StripComments(string content, string ext) => ext == ".css"
        ? Regex.Replace(content, @"/\*[\s\S]*?\*/", " ")
        : Regex.Replace(content, @"/\*[\s\S]*?\*/|(?<![:\\'""])//[^\n]*", " ");
}
