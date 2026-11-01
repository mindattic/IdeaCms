namespace MindAttic.Ideas.Abstractions;

// ============================================================================================
//  DISCOVERY — by CONVENTION, with an OPTIONAL override attribute.
//  A type becomes content by deriving from a kind base (PageBase/PluginBase/ThemeBase/ComponentBase).
//  Identity (Kind, Key, Version) is inferred:
//    • Kind    = which base it derives from.
//    • Key     = the namespace tail after IdeaCms.<Kind>.  (e.g. ...Module.Tooltip -> "tooltip")
//    • Version = the Vn class name.                                 (e.g. class V11           -> 11)
//  Use [Idea] only to OVERRIDE the convention (non-conforming names, or to set Scope=Global).
//  APPEND-ONLY: new optional init-only properties may be added; never remove/rename/retype.
// ============================================================================================

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class IdeaAttribute : Attribute
{
    /// <summary>Override the convention-inferred key (stable lowercase dotted id).</summary>
    public string? Key { get; init; }
    /// <summary>Override the convention-inferred whole-number version. 0 = infer from the Vn class name.</summary>
    public int Version { get; init; }
    public string? DisplayName { get; init; }
    public string? Category { get; init; }
    /// <summary>For Module/Control: Global attaches at theme scope; default Placeable.</summary>
    public PlacementScope Scope { get; init; } = PlacementScope.Placeable;
    public CmsRenderMode RenderMode { get; init; } = CmsRenderMode.InteractiveServer;
    /// <summary>
    /// For a Plugin activated site-wide: whether it renders before or after the theme/body. Read off the
    /// TYPE (no instantiation) by the host, so a footer plugin lands at the foot of the page instead of
    /// the top. Defaults to BeforeBody, which is where every plugin rendered before this existed.
    /// </summary>
    public PluginSlot Slot { get; init; } = PluginSlot.BeforeBody;

    public IdeaAttribute() { }
    public IdeaAttribute(string key) => Key = key;
}

/// <summary>
/// Declares a citizen this COMPILED Page/Theme references BY STRING ID at runtime (via
/// <see cref="CmsInclude"/>) but does NOT compile against. Repeatable. The reflection-only packer emits
/// each one into the manifest's <c>uses[]</c>, so the host can (1) hoist the referenced citizen's css/js
/// into <c>&lt;head&gt;</c> for this compiled page, (2) warn at install when a declared dependency is not
/// installed, and (3) reference-guard it against deletion. This is the declarative dependency model
/// (Orchard-style): a page never compile-references another package, it names what it uses.
/// <paramref name="version"/> 0 = float to the latest enabled version.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class UsesAttribute(ContentKind kind, string key, int version = 0) : Attribute
{
    public ContentKind Kind { get; } = kind;
    public string Key { get; } = key;
    /// <summary>Pinned whole-number version; 0 = float to latest.</summary>
    public int Version { get; } = version;
}

/// <summary>
/// Describes one INSTANCE SETTING (M.IDEA-§4.5). Every public, writable, typed <c>[Parameter]</c> of a citizen
/// (bool / string / number / enum, nullable allowed) is an instance setting whether or not it carries this
/// attribute; this attribute only adds the Admin-facing label, grouping and help, or hides/marks it.
/// A bool setting is how a citizen exposes an on/off feature toggle. Values are per INSTANCE: a Component's
/// live on its own tag, a Theme/Plugin/Code-Page's in that page's instance-settings slot. There is no
/// site-wide "by type" layer — configuration is shared by Admin Copy/Paste Configuration between instances.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class SettingAttribute : Attribute
{
    public SettingAttribute() { }
    public SettingAttribute(string displayName) => DisplayName = displayName;

    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    /// <summary>Admin editor section, e.g. "Effects", "Layout", "Colors".</summary>
    public string? Group { get; init; }
    public int Order { get; init; }
    /// <summary>
    /// False for CONTENT rather than configuration (an image id, a caption, a link target): the Admin
    /// "Copy Configuration" skips it, so pasting onto another instance keeps that instance's own content.
    /// </summary>
    public bool Copyable { get; init; } = true;
    /// <summary>Render a multi-line editor for a string setting.</summary>
    public bool Multiline { get; init; }
    /// <summary>Not an instance setting at all (host-wired plumbing); hidden from Admin.</summary>
    public bool Hidden { get; init; }
}

/// <summary>
/// Stamped on a content assembly by the SDK packer; the host reads it to gate package loads against
/// <see cref="Sdk.Version"/>. Whole-number SDK version (no SemVer).
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
public sealed class IdeaSdkVersionAttribute(int version) : Attribute
{
    public int Version { get; } = version;
}

/// <summary>The frozen Abstractions SDK version. MAJOR pinned at 1 forever; additive-only.</summary>
public static class Sdk
{
    /// <summary>Whole-number SDK version a host advertises and packages gate against.</summary>
    public const int Version = 1;
}
