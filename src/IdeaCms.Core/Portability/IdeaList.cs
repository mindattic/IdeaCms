namespace IdeaCms.Core.Portability;

/// <summary>
/// One artifact (M.IDEA-§4.9) that serves two
/// jobs: deployment provisioning (which `.idea` citizens a fresh instance installs, and what it seeds)
/// and ad-hoc content portability (move authored content between environments).
/// <para>
/// <see cref="Packages"/> is deployment-wide and installs in listed order. Everything else — <see
/// cref="Site"/>, <see cref="Settings"/>, <see cref="Pages"/>, <see cref="ComponentMetadata"/>, <see
/// cref="Media"/> — is exactly one site's content, never multi-site per file.
/// </para>
/// <para>
/// Identity travels on <see cref="Entities.ContentEntityBase.Uid"/>, the portable secondary identity;
/// integer ids are environment-local and never cross the boundary. Media is the exception — see
/// <see cref="IdeaListMedia"/> — because the store, not the list, mints a media uid.
/// </para>
/// </summary>
public sealed class IdeaList
{
    /// <summary>List format version. Whole numbers; a reader refuses a version it does not know.</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public const int CurrentFormatVersion = 1;

    /// <summary>The file inside the archive that carries this manifest.</summary>
    public const string ManifestEntryName = "idealist.json";

    /// <summary>Archive folder holding media payloads, one file per item, named by its source uid.</summary>
    public const string MediaFolder = "media/";

    public DateTime ExportedUtc { get; set; }

    /// <summary>Free text: which machine/environment produced this, for the import log.</summary>
    public string? ExportedFrom { get; set; }

    /// <summary>
    /// Deployment-wide package requirements, in the exact order they must install — a later entry may
    /// depend on one installed earlier in this same list. Grammar is
    /// <see cref="Rendering.IncludeReferenceParser.TryParseUse"/> ("Kind.key@version"), but UNLIKE
    /// requires[]/uses[] elsewhere, the version is MANDATORY here: a provisioning list pins an exact
    /// build, it never floats to latest.
    /// </summary>
    public List<string> Packages { get; set; } = [];

    public IdeaListSite? Site { get; set; }
    public List<IdeaListSetting> Settings { get; set; } = [];
    public List<IdeaListPage> Pages { get; set; } = [];
    public List<IdeaListComponentMetadata> ComponentMetadata { get; set; } = [];
    public List<IdeaListMedia> Media { get; set; } = [];
}

/// <summary>The site row. Matched on <see cref="Key"/>, not uid — "default" means the same thing everywhere.</summary>
public sealed class IdeaListSite
{
    public string Key { get; set; } = "default";
    public string Name { get; set; } = "";
    public string HostBindings { get; set; } = "";
    public string DefaultThemeKey { get; set; } = "";
    public int DefaultThemeVersion { get; set; } = 1;
    public string DefaultThemeMode { get; set; } = "light";
    public string? SettingsJson { get; set; }
}

/// <summary>
/// A setting from the override chain. Only Host- and Site-scope entries travel: a Page-scope entry
/// keys on an environment-local page id, and everything a page needs is already on the page row.
/// </summary>
public sealed class IdeaListSetting
{
    public string Scope { get; set; } = "Host";
    public string Key { get; set; } = "";
    public string? Value { get; set; }
}

/// <summary>An authored page, with everything that makes it render the way the author left it.</summary>
public sealed class IdeaListPage
{
    public Guid Uid { get; set; }
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string? SeoTitle { get; set; }

    /// <summary>Parent page's uid, so the nav tree survives without exporting integer ids.</summary>
    public Guid? ParentUid { get; set; }

    public string Kind { get; set; } = "Data";
    public string? BodyHtml { get; set; }
    public string? PageCss { get; set; }
    public string? PageJs { get; set; }

    /// <summary>
    /// Author or Untrusted. Import does NOT blindly trust this — see IdeaListImporter: a list is a
    /// file from elsewhere, and Author trust means raw markup passthrough.
    /// </summary>
    public string BodyTrust { get; set; } = "Untrusted";

    public string? ThemeKey { get; set; }
    public int? ThemeVersion { get; set; }
    public string? ActivePluginsJson { get; set; }

    public string? ComponentTypeName { get; set; }
    public string? AssemblyName { get; set; }
    public string? SettingsJson { get; set; }

    public bool IsPublished { get; set; }
    public bool Enabled { get; set; } = true;
    public bool IsRestricted { get; set; }
    public bool OpenInNewWindow { get; set; }
    public int SortOrder { get; set; }
    public string? WorkflowState { get; set; }

    public Dictionary<string, string> MetaTags { get; set; } = [];

    /// <summary>Role NAMES granted access. Users are deliberately not exported — accounts are per-environment.</summary>
    public List<string> RoleAccess { get; set; } = [];

    /// <summary>Old slugs that still resolve to this page, so 301s survive the move.</summary>
    public List<IdeaListSlugAlias> SlugHistory { get; set; } = [];

    /// <summary>
    /// The page's theme / plugin / code-page INSTANCE settings (M.IDEA-§4.5), one per slot. Component
    /// instances need no entry: their settings are attributes on their tags in <see cref="BodyHtml"/>.
    /// Null = the list predates instance settings, so import leaves the page's slots untouched; a present
    /// list (even empty) is the whole truth, so import removes slots it does not name.
    /// </summary>
    public List<IdeaListInstanceSettings>? InstanceSettings { get; set; }

    /// <summary>
    /// Every "Kind.key@version" this page's body/theme/active-plugins actually reference, pinned to the
    /// version active when exported. <see cref="IdeaListImporter"/> validates EVERY entry against the
    /// catalog AFTER <see cref="IdeaList.Packages"/> finishes installing and BEFORE any page is written;
    /// an unmet entry throws — no partial apply — rather than degrading through
    /// <c>M.IDEA-LAW-7</c>'s placeholder+Admin-Inbox path (that path stays for ordinary runtime rendering;
    /// it is not what a curated instance should rely on at provisioning time).
    /// </summary>
    public List<string> Uses { get; set; } = [];
}

public sealed class IdeaListSlugAlias
{
    public string OldSlug { get; set; } = "";
    public bool IsVanity { get; set; }
}

/// <summary>Per-component per-page metadata (e.g. FromMd's markdown + source path), keyed by page uid.</summary>
public sealed class IdeaListComponentMetadata
{
    public Guid PageUid { get; set; }
    public string ComponentKey { get; set; } = "";
    public string SlotName { get; set; } = "main";
    public string MetadataJson { get; set; } = "{}";
}

/// <summary>
/// A media item and the archive entry holding its bytes.
/// <para>
/// <see cref="SourceUid"/> is the uid the item had in the EXPORTING environment, and it is not a
/// promise about the importing one: <c>IMediaStore.UploadAsync</c> mints the uid, so an imported item
/// gets a new one. That is why import rewrites every <c>/_media/{uid}</c> reference through an
/// old→new map instead of forcing the uid, which would work for the local disk store and quietly
/// corrupt the Azure one (where the blob is addressed by uid).
/// </para>
/// </summary>
public sealed class IdeaListMedia
{
    public Guid SourceUid { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public string Folder { get; set; } = "";
    public string MediaType { get; set; } = "";
    public long SizeBytes { get; set; }
    /// <summary>Content hash — the reuse key, so a re-import uploads nothing.</summary>
    public string Sha256 { get; set; } = "";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Notes { get; set; }
    /// <summary>Path of the payload inside the archive, e.g. <c>media/3f2b…-….png</c>.</summary>
    public string EntryName { get; set; } = "";
}

/// <summary>One stored instance-settings slot of a page ("theme", "page", "plugin:{key}").</summary>
public sealed class IdeaListInstanceSettings
{
    public string Slot { get; set; } = "";
    public string WidgetRef { get; set; } = "";
    public string SettingsJson { get; set; } = "{}";
}
