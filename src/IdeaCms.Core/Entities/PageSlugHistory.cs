namespace IdeaCms.Core.Entities;

/// <summary>
/// Historical slug for a page. Written automatically when <see cref="Page.Slug"/> changes, and
/// optionally manually for vanity URLs. The router checks this table before returning 404, so renamed
/// pages never become dead links. Responds with a client redirect to the page's current slug.
/// Unique per (PageId, OldSlug): the same old slug is never recorded twice for the same page.
/// </summary>
public sealed class PageSlugHistory
{
    public int Id { get; set; }
    public int PageId { get; set; }
    public string OldSlug { get; set; } = "";       // the slug that used to (or additionally) resolve this page
    public bool IsVanity { get; set; }              // true = manually added vanity/alias URL; false = auto-rename
    public string? AddedByUserId { get; set; }      // null = auto-created on rename; set = manual vanity add
    public DateTime CreatedUtc { get; set; }
}
