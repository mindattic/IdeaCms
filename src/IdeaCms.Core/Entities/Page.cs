using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Core.Entities;

/// <summary>
/// A free-form page. ONE table for both render kinds (Data | Code). No zones, panes, or slots.
/// System-versioned (temporal) so every prior state — including which Theme/Component versions it
/// pinned — is recoverable (wiki-like history). Resolved by (SiteId, Slug); never per-page routing.
/// </summary>
public sealed class Page : ContentEntityBase
{
    public int? SiteId { get; set; }                 // nullable -> multi-tenant is additive
    public int? ParentId { get; set; }               // tree -> nav menu
    public string Slug { get; set; } = "";           // "" = home; unique per (SiteId, Slug)
    public string Title { get; set; } = "";

    public string? ThemeKey { get; set; }            // page override; else the site default
    public int? ThemeVersion { get; set; }           // pinned whole-number theme version

    /// <summary>
    /// JSON array of plugin ref strings selected for this page (e.g. <c>["Plugin.tooltip","Plugin.navmenu@1"]</c>).
    /// Null = no plugins selected. Serialized/deserialized by PageAdminService; rendered by PageHost before body.
    /// </summary>
    public string? ActivePluginsJson { get; set; }

    public PageKind Kind { get; set; }               // the discriminator

    // ---- Data page (null for Code pages): the free-form author content ----
    public string? BodyHtml { get; set; }            // may contain <ma-component key=... v=...> includes
    public string? PageCss { get; set; }             // cascade tier 3 (page-level <style>)
    public string? PageJs { get; set; }              // intentional author JS (emitted only when Author-trusted)
    public ContentTrust BodyTrust { get; set; }      // stamped at WRITE time from the author's claim
    public string? AuthoredByUserId { get; set; }    // who stamped the trust
    public int AuthorTrustVersion { get; set; }      // epoch: bump to bulk re-gate without a schema change

    // ---- Code page (null for Data pages) ----
    public string? ComponentTypeName { get; set; }   // resolved late via ITypeResolver
    public string? AssemblyName { get; set; }
    public string? SettingsJson { get; set; }

    // ---- shared ----
    /// <summary>When true, navigation links to this page should open in a new browser tab/window.</summary>
    public bool OpenInNewWindow { get; set; }
    public bool IsPublished { get; set; }
    /// <summary>Disabled = exists but cannot be served until re-enabled.</summary>
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    /// <summary>Overrides the browser &lt;title&gt; tag. Null = fall back to <see cref="Title"/>.</summary>
    public string? SeoTitle { get; set; }

    public ICollection<PageMetaTag> MetaTags { get; set; } = [];

    /// <summary>Set when this page arrived from an installed .idea package (reserved for Phase 5).</summary>
    public int? SourcePackageId { get; set; }

    // ---- Access control ----
    /// <summary>False (default) = public. True = only users whose role or ID appears in the access lists can view.</summary>
    public bool IsRestricted { get; set; }

    public ICollection<PageRoleAccess> RoleAccess { get; set; } = [];
    public ICollection<PageUserAccess> UserAccess { get; set; } = [];

    // ---- Content workflow ----
    /// <summary>
    /// FK to <see cref="WorkflowDefinition"/>. Null = site default workflow (or no workflow when no default
    /// is configured). When set, <see cref="WorkflowService"/> enforces role-gated transitions between states.
    /// </summary>
    public int? WorkflowDefinitionId { get; set; }

    /// <summary>
    /// Current workflow state name (e.g. "Draft", "Published"). Null = legacy behavior — governed by
    /// <see cref="IsPublished"/> only. Set by <see cref="Services.WorkflowService"/> on each transition.
    /// Transitioning to a state named "Published" also sets <see cref="IsPublished"/> = true.
    /// </summary>
    public string? WorkflowState { get; set; }

    // ---- Slug history ----
    public ICollection<PageSlugHistory> SlugHistory { get; set; } = [];
}
