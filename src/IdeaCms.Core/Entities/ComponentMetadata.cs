namespace IdeaCms.Core.Entities;

/// <summary>
/// Per-component per-page metadata blob. Keyed by (PageUid, ComponentKey, SlotName); one row per
/// placed instance. The MetadataJson is component-defined — the FromMd component stores
/// { "localSourceFile": "…", "markdown": "…", "lastSynced": "…" }.
/// </summary>
public sealed class ComponentMetadata
{
    public int Id { get; set; }
    /// <summary>Stable page identity from <see cref="ContentEntityBase.Uid"/> / <see cref="IPageContext.PageId"/>.</summary>
    public Guid PageUid { get; set; }
    /// <summary>Lowercase component key, e.g. "frommd".</summary>
    public string ComponentKey { get; set; } = "";
    /// <summary>Instance discriminator within the page. "main" when there is only one instance.</summary>
    public string SlotName { get; set; } = "main";
    /// <summary>Component-owned JSON bag. Schema is component-defined.</summary>
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
}
