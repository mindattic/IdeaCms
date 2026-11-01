using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdeaCms.Component.Frontpage;

/// <summary>One navigable item in a section's tab rail (a project, book, link, …).</summary>
public sealed class HubPage
{
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Tagline { get; init; }
    /// <summary>External "Open" link (project site, Amazon page, portfolio URL).</summary>
    public string? OpenUrl { get; init; }
    public string? GitHubUrl { get; init; }
    /// <summary>Cover image filename under <c>covers/</c>, if any (books/art); null renders a placeholder.</summary>
    public string? Cover { get; init; }
}

/// <summary>An accordion section: Portfolio, Software, Ecosystem, Hardware, Writing, Visual Arts.</summary>
public sealed class HubSection
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>links | cards | diagram | books | art — drives the panel layout.</summary>
    public string Kind { get; init; } = "";
    public List<HubPage> Pages { get; init; } = [];
    /// <summary>Raw inline HTML for the <c>diagram</c> section (the ecosystem SVG). Set at load time.</summary>
    public string? IntroHtml { get; set; }
}

internal sealed class HubData
{
    public List<HubSection> Sections { get; init; } = [];
}

/// <summary>
/// Loads the bundled, self-contained hub content from embedded resources — no database. The
/// accordion data lives in <c>Data/hub.json</c>; the ecosystem diagram in <c>Data/ecosystem.svg</c>;
/// the stylesheet in <c>Resources/frontpage.css</c>. All three ship inside the assembly so the
/// <c>.idea</c> is self-describing.
/// </summary>
public static class HubContent
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static List<HubSection>? _sections;
    private static string? _css;

    public static IReadOnlyList<HubSection> Sections => _sections ??= Load();
    public static string Css => _css ??= ReadResource("frontpage.css");

    private static List<HubSection> Load()
    {
        var data = JsonSerializer.Deserialize<HubData>(ReadResource("hub.json"), Json) ?? new HubData();
        var svg = ReadResource("ecosystem.svg");
        foreach (var s in data.Sections)
            if (s.Kind == "diagram")
                s.IntroHtml = svg;
        return data.Sections;
    }

    private static string ReadResource(string suffix)
    {
        var asm = typeof(HubContent).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource '{suffix}' not found.");
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// The Frontpage instance settings (M.IDEA-§4.5) that SiteNav needs at render time. Defaults reproduce the
/// as-designed behaviour; V1 builds one from its [Parameter]s.
/// </summary>
public sealed class SiteNavOptions
{
    public string? RootClass { get; init; }
    public string? RootStyle { get; init; }
    /// <summary>Section key open on load; null = the first section.</summary>
    public string? DefaultSection { get; init; }
    public bool StartCollapsed { get; init; }
    public bool AllowCollapse { get; init; } = true;
    public bool ShowCounts { get; init; } = true;
    public bool ShowCovers { get; init; } = true;
    public bool ShowTaglines { get; init; } = true;
    public bool ShowTooltips { get; init; } = true;
    public bool ShowOpenLink { get; init; } = true;
    public bool ShowGitHubLink { get; init; } = true;
    public string OpenLabel { get; init; } = "Open";
    public string GitHubLabel { get; init; } = "GitHub";
    public string EmptyText { get; init; } = "Nothing here yet.";
}
