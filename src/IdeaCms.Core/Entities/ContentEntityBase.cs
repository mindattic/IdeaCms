namespace IdeaCms.Core.Entities;

/// <summary>
/// Reserved columns present on every primary content entity from migration #1, so future features
/// never require a destructive schema change. (Adding NEW tables/columns later is additive and allowed;
/// what we avoid is ever needing to alter or drop these.)
/// </summary>
public abstract class ContentEntityBase
{
    /// <summary>Environment-local identity. NEVER crosses an import/export boundary.</summary>
    public int Id { get; set; }

    /// <summary>Stable portable secondary identity (for .idea import/seed reconciliation).</summary>
    public Guid Uid { get; set; } = Guid.NewGuid();

    /// <summary>Soft delete — rows are never hard-deleted by the app.</summary>
    public bool IsDeleted { get; set; }
    public DateTime? DeletedUtc { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    /// <summary>Reserved JSON extensibility bag for additive fields that don't yet warrant a column.</summary>
    public string? Extra { get; set; }

    /// <summary>Optimistic-concurrency token (ported pattern from MindAttic.Frontpage).</summary>
    public byte[]? RowVersion { get; set; }
}
