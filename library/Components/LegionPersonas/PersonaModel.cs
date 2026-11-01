using System.Reflection;
using System.Text.Json;

namespace IdeaCms.Component.LegionPersonas;

/// <summary>
/// A flattened, display-ready persona — the projection the gallery needs, with no MindAttic.Legion
/// dependency. <see cref="Shape"/> is the persona's precomputed SacredGeometry shape index; the
/// psychometric <c>*Line</c> fields are pre-formatted at export time to match the original detail view.
/// </summary>
public sealed record Persona(
    string Id,
    string Name,
    int Shape,
    string? Archetype,
    string? Worldview,
    string? Background,
    int? Age,
    string? Pronouns,
    string? Quirk,
    string Bio,
    bool HasProfile,
    string? MbtiType,
    string? MbtiLine,
    int? EnneagramType,
    string? EnneagramLine,
    string? DiscPrimary,
    string? DiscLine,
    string? OceanLine,
    string? HexacoLine,
    string? ScoredLine);

/// <summary>
/// The persona library, deserialized once from the embedded <c>personas.json</c> resource, plus the
/// distinct facet values the filter bar offers. Static — shared across all circuits.
/// </summary>
public static class PersonaData
{
    public static IReadOnlyList<Persona> All { get; }
    public static int ScoredCount { get; }

    public static IReadOnlyList<string> Archetypes { get; }
    public static IReadOnlyList<string> Worldviews { get; }
    public static IReadOnlyList<string> Backgrounds { get; }
    public static IReadOnlyList<string> Pronouns { get; }
    public static IReadOnlyList<string> MbtiTypes { get; }
    public static IReadOnlyList<string> DiscStyles { get; }
    public static IReadOnlyList<int> EnneagramTypes { get; }

    static PersonaData()
    {
        var asm = typeof(PersonaData).Assembly;
        var resource = asm.GetManifestResourceNames()
            .First(n => n.EndsWith("personas.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resource)!;
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        All = JsonSerializer.Deserialize<List<Persona>>(stream, opts) ?? new List<Persona>();

        ScoredCount = All.Count(p => p.HasProfile);
        Archetypes  = Distinct(p => p.Archetype);
        Worldviews  = Distinct(p => p.Worldview);
        Backgrounds = Distinct(p => p.Background);
        Pronouns    = Distinct(p => p.Pronouns);
        MbtiTypes   = Distinct(p => p.MbtiType);
        DiscStyles  = Distinct(p => p.DiscPrimary);
        EnneagramTypes = All.Where(p => p.EnneagramType is not null)
            .Select(p => p.EnneagramType!.Value).Distinct().OrderBy(x => x).ToList();
    }

    private static List<string> Distinct(Func<Persona, string?> selector) =>
        All.Select(selector).Where(v => !string.IsNullOrEmpty(v)).Select(v => v!)
            .Distinct().OrderBy(v => v, StringComparer.Ordinal).ToList();
}

/// <summary>The current filter state bound to the gallery's controls. All facets AND together.</summary>
public sealed class PersonaFilter
{
    public string Text { get; set; } = "";
    public string Archetype { get; set; } = "";
    public string Worldview { get; set; } = "";
    public string Background { get; set; } = "";
    public string Pronouns { get; set; } = "";
    public string MbtiType { get; set; } = "";
    public string DiscPrimary { get; set; } = "";
    public int? EnneagramType { get; set; }
    public int MinAge { get; set; }
    public int MaxAge { get; set; } = 120;
}

/// <summary>Pure filtering of personas by a <see cref="PersonaFilter"/>.</summary>
public static class PersonaFiltering
{
    public static IEnumerable<Persona> Apply(IEnumerable<Persona> source, PersonaFilter f)
    {
        IEnumerable<Persona> q = source;

        if (!string.IsNullOrWhiteSpace(f.Text))
        {
            var t = f.Text.Trim();
            q = q.Where(p => Contains(p.Name, t) || Contains(p.Archetype, t) || Contains(p.Worldview, t)
                          || Contains(p.Background, t) || Contains(p.Quirk, t) || Contains(p.Bio, t));
        }
        if (Has(f.Archetype))   q = q.Where(p => p.Archetype == f.Archetype);
        if (Has(f.Worldview))   q = q.Where(p => p.Worldview == f.Worldview);
        if (Has(f.Background))   q = q.Where(p => p.Background == f.Background);
        if (Has(f.Pronouns))     q = q.Where(p => p.Pronouns == f.Pronouns);
        if (Has(f.MbtiType))     q = q.Where(p => p.MbtiType == f.MbtiType);
        if (Has(f.DiscPrimary))  q = q.Where(p => p.DiscPrimary == f.DiscPrimary);
        if (f.EnneagramType is not null) q = q.Where(p => p.EnneagramType == f.EnneagramType);
        if (f.MinAge > 0)        q = q.Where(p => p.Age is null || p.Age >= f.MinAge);
        if (f.MaxAge < 120)      q = q.Where(p => p.Age is null || p.Age <= f.MaxAge);

        return q;
    }

    private static bool Has(string? v) => !string.IsNullOrEmpty(v);

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
