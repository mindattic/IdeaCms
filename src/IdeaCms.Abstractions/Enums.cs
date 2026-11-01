namespace IdeaCms.Abstractions;

// ============================================================================================
//  FROZEN ENUMS — explicit ascending integer values, APPEND-ONLY forever.
//  Never reorder, never renumber, never remove. New members (incl. new content kinds) are
//  appended at the end with the next integer. A host MUST treat an unknown ordinal as the safe
//  default (Static / View / Placeable / etc.).
// ============================================================================================

/// <summary>
/// The content kinds. The kind is determined by which base a type derives from
/// (<see cref="PageBase"/> / <see cref="PluginBase"/> / <see cref="ThemeBase"/> / <see cref="ComponentBase"/>).
/// "Idea" is the package format (<c>.idea</c>) and the shared base
/// (<see cref="IdeaBase"/>), never a kind. New kinds may be APPENDED here without a breaking change.
/// </summary>
public enum ContentKind
{
    Page = 0,
    Plugin = 1,      // site-wide .idea: activates a behavior/capability across the whole page (M.IDEA-LAW-1)
    Theme = 2,
    // Ordinal 3 is reserved and NEVER reused (M.IDEA-LAW-2) — atomic UI is authored as a Component.
    Component = 4,   // inline-placed .idea: renders at the <Component.X /> tag position; can nest (M.IDEA-LAW-1)
}

/// <summary>How a Page renders. A Page row carries exactly one of these.</summary>
public enum PageKind
{
    /// <summary>Free-form author HTML/CSS/JS stored in the DB; rendered dynamically, zero deploy.</summary>
    Data = 0,
    /// <summary>A compiled <see cref="PageBase"/> subclass; deploys once per type.</summary>
    Code = 1,
}

/// <summary>
/// Interactivity mode a content type runs under. WebAssembly is intentionally EXCLUDED: a
/// runtime-loaded <c>.idea</c> assembly cannot reach the browser WASM runtime (a hard .NET boundary).
/// If Blazor ever makes WASM runtime-loadable, a new member is appended here — purely additive.
/// </summary>
public enum CmsRenderMode
{
    Static = 0,
    InteractiveServer = 1,
}

/// <summary>View vs the shared Edit/Preview chrome (same render path, chrome toggled by mode).</summary>
public enum ContentMode
{
    View = 0,
    Edit = 1,
    Preview = 2,
}

/// <summary>Where a registration came from. Compiled wins collisions over Package (see Priority).</summary>
public enum ContentOrigin
{
    Compiled = 0,
    Package = 1,
}

/// <summary>How the host turns a descriptor into rendered output.</summary>
public enum RenderStrategy
{
    /// <summary>Resolve a CLR <see cref="System.Type"/> and render via <c>DynamicComponent</c>.</summary>
    ClrType = 0,
    /// <summary>Render free-form author markup through the built-in FreeFormPage / raw-content gate.</summary>
    RawMarkup = 1,
}

/// <summary>Where a placeable content type (Module/Control) attaches.</summary>
public enum PlacementScope
{
    /// <summary>Dropped into a page by a <c>&lt;IdeaCms.…&gt;</c> include tag.</summary>
    Placeable = 0,
    /// <summary>Attached globally at theme scope (e.g. a page scrollbar, fonts).</summary>
    Global = 1,
}

/// <summary>
/// Where in the page a globally-activated Plugin renders, relative to the theme/body.
/// APPEND-ONLY: ordinals grow by appending; never renumber.
/// </summary>
public enum PluginSlot
{
    /// <summary>Before the theme/body — headers, nav, font and behavior activators. The default, and
    /// the only behavior that existed before this enum, so every existing plugin keeps its position.</summary>
    BeforeBody = 0,
    /// <summary>After the theme/body — footers and anything that must close the page.</summary>
    AfterBody = 1,
}

/// <summary>
/// The trust line for author-supplied raw markup/JS. Stamped at WRITE time from the author's claim,
/// never re-evaluated at render against the current viewer.
/// </summary>
public enum ContentTrust
{
    /// <summary>Sanitized on render (HtmlSanitizer).</summary>
    Untrusted = 0,
    /// <summary>Rendered verbatim — intentional admin-authored HTML/JS is honored.</summary>
    Author = 1,
}
