namespace IdeaCms.Core.Entities;

/// <summary>
/// A named meta-tag value attached to a page (e.g. "seo.title", "seo.description", "og:image").
/// Replaces the old Page.SeoMetaJson blob. One row per (PageId, Name); unique index enforces it.
/// </summary>
public sealed class PageMetaTag
{
    public int Id { get; set; }
    public int PageId { get; set; }
    /// <summary>Dot-namespaced key, e.g. "seo.title", "seo.description", "og:image".</summary>
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
}
