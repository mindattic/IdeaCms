using Microsoft.EntityFrameworkCore;
using IdeaCms.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;

namespace IdeaCms.Core.Services;

public sealed class ComponentMetadataService(IDbContextFactory<CmsDbContext> dbFactory) : IComponentMetadataStore
{
    public async Task<string?> GetAsync(Guid pageUid, string componentKey, string slotName = "main", CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ComponentMetadata
            .Where(m => m.PageUid == pageUid && m.ComponentKey == componentKey && m.SlotName == slotName)
            .Select(m => (string?)m.MetadataJson)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>One query for a whole list of pages — the default interface impl would issue one per row.</summary>
    public async Task<IReadOnlyDictionary<Guid, string>> GetManyAsync(
        IEnumerable<Guid> pageUids, string componentKey, string slotName = "main", CancellationToken ct = default)
    {
        var ids = pageUids.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, string>();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.ComponentMetadata
                .Where(m => ids.Contains(m.PageUid) && m.ComponentKey == componentKey && m.SlotName == slotName)
                .ToDictionaryAsync(m => m.PageUid, m => m.MetadataJson, ct);
        }
        catch
        {
            return new Dictionary<Guid, string>();   // never throw into a render
        }
    }

    public async Task SaveAsync(Guid pageUid, string componentKey, string slotName, string metadataJson, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        // Retry once: two admin tabs saving simultaneously both see existing=null, both try to insert,
        // and the second SaveChangesAsync hits the UNIQUE index. On the retry the winner's row is visible.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var existing = await db.ComponentMetadata
                .FirstOrDefaultAsync(m => m.PageUid == pageUid && m.ComponentKey == componentKey && m.SlotName == slotName, ct);
            if (existing is null)
            {
                db.ComponentMetadata.Add(new ComponentMetadata
                {
                    PageUid = pageUid, ComponentKey = componentKey, SlotName = slotName,
                    MetadataJson = metadataJson, CreatedUtc = now, ModifiedUtc = now,
                });
            }
            else
            {
                existing.MetadataJson = metadataJson;
                existing.ModifiedUtc = now;
            }
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException) when (attempt == 0 && existing is null)
            {
                // Concurrent insert won the race — retry with a fresh context to load the winner's row.
            }
        }
    }
}
