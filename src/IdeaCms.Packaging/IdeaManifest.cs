using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdeaCms.Packaging;

/// <summary>
/// The <c>idea.json</c> manifest the packer emits and the host reads to register a package. This is a
/// FROZEN WIRE CONTRACT — once a <c>.idea</c> ships, the kernel field names and meanings cannot change,
/// only grow (forward-compat is preserved by <see cref="Extra"/>, which losslessly round-trips any field
/// a newer packer adds that this host doesn't yet model). See M.IDEA-§4.8 and
/// HOUSE-LAW-5. JSON keys are camelCase.
/// </summary>
public sealed record IdeaManifest
{
    // ---- Kernel: the six fields every package MUST carry. ----

    /// <summary>Schema version of <c>idea.json</c>. The host refuses a manifest newer than it understands.</summary>
    [JsonPropertyName("manifestVersion")] public int ManifestVersion { get; init; } = 1;

    /// <summary>The <see cref="IdeaCms.Abstractions.ContentKind"/> name: Page | Plugin | Theme | Component.</summary>
    [JsonPropertyName("category")] public string Category { get; init; } = "";

    /// <summary><c>data</c> (free-form author content, no assembly) or <c>code</c> (a compiled citizen in bin/).</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = "";

    [JsonPropertyName("key")] public string Key { get; init; } = "";

    /// <summary>Whole-number content version. Coexists with other versions of the same key.</summary>
    [JsonPropertyName("version")] public int Version { get; init; }

    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = "";

    // ---- Optional metadata (code packages and forward-compat). ----

    [JsonPropertyName("description")] public string Description { get; init; } = "";

    /// <summary>Abstractions SDK version this package was built against; the host gates code loads on it.</summary>
    [JsonPropertyName("sdk")] public int? Sdk { get; init; }

    /// <summary>For <c>code</c>: the resolvable CLR entry type full name. MUST be null for <c>data</c>.</summary>
    [JsonPropertyName("entryType")] public string? EntryType { get; init; }

    [JsonPropertyName("assemblyName")] public string AssemblyName { get; init; } = "";
    [JsonPropertyName("renderMode")] public string RenderMode { get; init; } = "InteractiveServer";
    [JsonPropertyName("scope")] public string Scope { get; init; } = "Placeable";

    /// <summary>Stylesheet asset paths, cascade order preserved (pre-shapes the Phase-5 head dedup).</summary>
    [JsonPropertyName("css")] public IReadOnlyList<string> Css { get; init; } = [];
    /// <summary>Script asset paths, load order preserved.</summary>
    [JsonPropertyName("scripts")] public IReadOnlyList<string> Scripts { get; init; } = [];
    /// <summary>Every static asset bundled under wwwroot/, relative paths preserved.</summary>
    [JsonPropertyName("assets")] public IReadOnlyList<string> Assets { get; init; } = [];
    /// <summary>The non-host assembly simple-names bundled in bin/ (informational; the loader audits bin/ itself).</summary>
    [JsonPropertyName("dependsOn")] public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>
    /// Citizens this COMPILED page/theme references BY STRING ID at runtime (via <c>CmsInclude</c>) but
    /// does NOT bundle — entries are <c>"&lt;Kind&gt;.&lt;key&gt;[@&lt;version&gt;]"</c>, e.g.
    /// <c>"Plugin.tooltip"</c>, <c>"Component.textbox@1"</c>. The packer derives these from
    /// <c>[Uses]</c>. The host hoists their css/js into <c>&lt;head&gt;</c>, warns if one is not installed,
    /// and reference-guards them against deletion. Omitted version = float to latest.
    /// </summary>
    [JsonPropertyName("uses")] public IReadOnlyList<string> Uses { get; init; } = [];

    /// <summary>
    /// The citizen's declared INSTANCE SETTINGS (M.IDEA-§4.5): its simple-typed [Parameter]s with their
    /// [Setting] metadata, read reflection-only by the packer. Informational — the host reflects the
    /// loaded type for the live schema — so a package describes its configuration surface without loading.
    /// </summary>
    [JsonPropertyName("settings")] public IReadOnlyList<IdeaManifestSetting> Settings { get; init; } = [];

    /// <summary>A package may shadow a compiled key only with this set true PLUS admin confirmation.</summary>
    [JsonPropertyName("allowOverride")] public bool AllowOverride { get; init; }

    /// <summary>
    /// Minimum host engine (CMS) version required to install this package. The host rejects install if its engine
    /// version is below this value. Null = no minimum. Compare against <see cref="HostEngineVersion"/>.
    /// </summary>
    [JsonPropertyName("minHostVersion")] public int? MinHostVersion { get; init; }

    /// <summary>
    /// Other installed packages this package REQUIRES to be installed and enabled before installation succeeds.
    /// Entries use <c>"Category.key"</c> or <c>"Category.key@version"</c> format, e.g.
    /// <c>"Plugin.tooltip"</c>, <c>"Plugin.tooltip@1"</c>. A missing required dependency is a hard install-time
    /// rejection (unlike <see cref="Uses"/>, which is advisory). Parse with
    /// <c>IncludeReferenceParser.ParseUses</c>.
    /// </summary>
    [JsonPropertyName("requires")] public IReadOnlyList<string> Requires { get; init; } = [];

    /// <summary>
    /// Optional name of the <c>WorkflowDefinition</c> this widget type suggests for its instances. When set,
    /// the host surfaces this as a hint in the admin UI; it does not auto-create the workflow.
    /// </summary>
    [JsonPropertyName("defaultWorkflow")] public string? DefaultWorkflow { get; init; }

    /// <summary>Forward-compat: any field a newer packer emits that this host doesn't model is captured here and re-serialized losslessly.</summary>
    [JsonExtensionData] public IDictionary<string, JsonElement> Extra { get; init; } = new Dictionary<string, JsonElement>();

    // ---- Host limits + the one canonical serializer. ----

    /// <summary>The highest <see cref="ManifestVersion"/> this host build can read.</summary>
    public const int HostMaxManifestVersion = 1;

    /// <summary>The Abstractions SDK version this host build provides; a code package's <see cref="Sdk"/> must be ≤ this.</summary>
    public const int HostSdkVersion = 1;

    /// <summary>The CMS engine version this host build provides. A package's <see cref="MinHostVersion"/> must be ≤ this.</summary>
    public const int HostEngineVersion = 1;

    /// <summary>The single serializer used for every read and write of an <c>idea.json</c>.</summary>
    public static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>One declared instance setting in <see cref="IdeaManifest.Settings"/>.</summary>
public sealed record IdeaManifestSetting
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    /// <summary>bool | string | integer | number | enum</summary>
    [JsonPropertyName("type")] public string Type { get; init; } = "string";
    [JsonPropertyName("displayName")] public string? DisplayName { get; init; }
    [JsonPropertyName("group")] public string? Group { get; init; }
    [JsonPropertyName("copyable")] public bool Copyable { get; init; } = true;
}
