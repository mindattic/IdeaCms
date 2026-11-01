namespace IdeaCms.Core.Entities;

/// <summary>
/// Host-managed per-placement settings for a named component slot on a page. Stores settings outside of the
/// page <see cref="Page.BodyHtml"/> token attributes, enabling independent versioning and rollback without
/// re-saving the full page body. Keyed by (PageId, SlotName) where SlotName is author-assigned
/// (e.g. "hero", "sidebar-cta"). One record per placement; history is in
/// <see cref="WidgetPlacementSettingsHistory"/>.
/// </summary>
public sealed class WidgetPlacementSettings
{
    public int Id { get; set; }
    public Guid Uid { get; set; } = Guid.NewGuid();
    public int PageId { get; set; }
    public string SlotName { get; set; } = "";         // author-defined placement identifier within the page
    public string WidgetRef { get; set; } = "";        // e.g. "Component.hero@1" or "Plugin.tooltip@1" — which citizen owns this slot
    public string SettingsJson { get; set; } = "{}";  // component-specific settings bag (schema is component-defined)
    public int SettingsVersion { get; set; } = 1;     // increments on every save; monotonic, never reused
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public string? ModifiedByUserId { get; set; }

    public ICollection<WidgetPlacementSettingsHistory> History { get; set; } = [];
}

/// <summary>
/// An immutable version snapshot written on every save of <see cref="WidgetPlacementSettings"/>.
/// Enables point-in-time rollback without a temporal table.
/// </summary>
public sealed class WidgetPlacementSettingsHistory
{
    public int Id { get; set; }
    public int PlacementSettingsId { get; set; }
    public string WidgetRef { get; set; } = "";
    public string SettingsJson { get; set; } = "{}";
    public int SettingsVersion { get; set; }
    public DateTime SavedUtc { get; set; }
    public string? SavedByUserId { get; set; }
}
