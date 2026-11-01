using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Core.Entities;

/// <summary>
/// The persisted catalog row for one registered citizen (a Code Page, a Component, or a Theme), from
/// any source. Identity is (Kind, Key, Version, Origin). Compiled wins a collision over Package by
/// Priority; the loser is kept VISIBLE as <see cref="IsShadowed"/> (never silently dropped).
/// <see cref="Enabled"/> drives the admin disable/enable lifecycle; <see cref="IsActive"/> is flipped
/// false by discovery when a previously-seen citizen disappears (degrade to a placeholder, never delete).
/// </summary>
public sealed class CmsContentDefinition
{
    public int Id { get; set; }
    public Guid Uid { get; set; } = Guid.NewGuid();

    public ContentKind Kind { get; set; }
    public string Key { get; set; } = "";
    public int Version { get; set; } = 1;            // whole-number; versions coexist
    public ContentOrigin Origin { get; set; }

    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "General";
    public RenderStrategy Strategy { get; set; } = RenderStrategy.ClrType;
    public CmsRenderMode RenderMode { get; set; } = CmsRenderMode.InteractiveServer;
    public PlacementScope Scope { get; set; } = PlacementScope.Placeable;

    public string? ClrTypeName { get; set; }         // resolved late; stale -> placeholder
    public string AssemblyName { get; set; } = "";
    public string? RawBundleJson { get; set; }       // for Strategy=RawMarkup citizens
    public string? AssetMount { get; set; }

    public int Priority { get; set; }                // Compiled=100, Package=50 by convention
    public bool IsShadowed { get; set; }             // lost a collision but kept visible
    public bool IsActive { get; set; } = true;       // discovery-managed presence
    /// <summary>Admin disable: a disabled Theme/Component cannot be used until re-enabled (HOUSE-LAW-2).</summary>
    public bool Enabled { get; set; } = true;
    public bool AllowOverride { get; set; }          // a package may shadow a compiled key only if true

    public DateTime DiscoveredUtc { get; set; }
    public byte[]? RowVersion { get; set; }
}
