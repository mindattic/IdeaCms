using Microsoft.EntityFrameworkCore;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;

namespace IdeaCms.Core.Services;

/// <summary>
/// Host-managed per-placement widget settings with explicit version history and rollback.
/// Each named slot on a page has one current <see cref="WidgetPlacementSettings"/> row; every save
/// appends an immutable <see cref="WidgetPlacementSettingsHistory"/> snapshot so any prior version
/// can be restored.
/// </summary>
public interface IWidgetInstanceSettingsService
{
    /// <summary>Returns the current settings for a named slot on a page, or null if none exist yet.</summary>
    Task<WidgetPlacementSettings?> GetAsync(int pageId, string slotName, CancellationToken ct = default);

    /// <summary>
    /// Upsert settings for a named slot. On create, initializes version 1. On update, bumps the version
    /// and appends a history snapshot of the PREVIOUS settings (so history always trails one behind the current
    /// row — the current row IS the latest version, no duplication).
    /// </summary>
    Task<WidgetPlacementSettings> SaveAsync(
        int pageId, string slotName, string widgetRef, string settingsJson,
        string? userId = null, CancellationToken ct = default);

    /// <summary>All history snapshots for a slot, newest version first.</summary>
    Task<IReadOnlyList<WidgetPlacementSettingsHistory>> GetHistoryAsync(int pageId, string slotName, CancellationToken ct = default);

    /// <summary>
    /// Rolls back the current settings to a specific historical version. Writes a new history snapshot of the
    /// pre-rollback state so the rollback itself is auditable. Returns false if the slot or version is not found.
    /// </summary>
    Task<bool> RollbackAsync(int pageId, string slotName, int version, string? userId = null, CancellationToken ct = default);
}

public sealed class WidgetInstanceSettingsService(IDbContextFactory<CmsDbContext> dbFactory) : IWidgetInstanceSettingsService
{
    public async Task<WidgetPlacementSettings?> GetAsync(int pageId, string slotName, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WidgetPlacementSettings
            .Include(s => s.History)
            .FirstOrDefaultAsync(s => s.PageId == pageId && s.SlotName == slotName, ct);
    }

    public async Task<WidgetPlacementSettings> SaveAsync(
        int pageId, string slotName, string widgetRef, string settingsJson,
        string? userId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var existing = await db.WidgetPlacementSettings
            .FirstOrDefaultAsync(s => s.PageId == pageId && s.SlotName == slotName, ct);

        if (existing is null)
        {
            existing = new WidgetPlacementSettings
            {
                PageId = pageId, SlotName = slotName, WidgetRef = widgetRef,
                SettingsJson = settingsJson, SettingsVersion = 1,
                CreatedUtc = now, ModifiedUtc = now, ModifiedByUserId = userId,
            };
            db.WidgetPlacementSettings.Add(existing);
            await db.SaveChangesAsync(ct);
            return existing;
        }

        // Snapshot the pre-save state into history before overwriting.
        db.WidgetPlacementSettingsHistory.Add(new WidgetPlacementSettingsHistory
        {
            PlacementSettingsId = existing.Id,
            WidgetRef = existing.WidgetRef,
            SettingsJson = existing.SettingsJson,
            SettingsVersion = existing.SettingsVersion,
            SavedUtc = now,
            SavedByUserId = userId,
        });

        existing.WidgetRef = widgetRef;
        existing.SettingsJson = settingsJson;
        existing.SettingsVersion += 1;
        existing.ModifiedUtc = now;
        existing.ModifiedByUserId = userId;

        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<IReadOnlyList<WidgetPlacementSettingsHistory>> GetHistoryAsync(
        int pageId, string slotName, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settingsId = await db.WidgetPlacementSettings
            .Where(s => s.PageId == pageId && s.SlotName == slotName)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);

        if (settingsId is null) return [];

        return await db.WidgetPlacementSettingsHistory
            .Where(h => h.PlacementSettingsId == settingsId)
            .OrderByDescending(h => h.SettingsVersion)
            .ToListAsync(ct);
    }

    public async Task<bool> RollbackAsync(
        int pageId, string slotName, int version, string? userId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var existing = await db.WidgetPlacementSettings
            .FirstOrDefaultAsync(s => s.PageId == pageId && s.SlotName == slotName, ct);
        if (existing is null) return false;

        var target = await db.WidgetPlacementSettingsHistory
            .FirstOrDefaultAsync(h => h.PlacementSettingsId == existing.Id && h.SettingsVersion == version, ct);
        if (target is null) return false;

        // Snapshot current state before rolling back so the rollback itself is auditable.
        db.WidgetPlacementSettingsHistory.Add(new WidgetPlacementSettingsHistory
        {
            PlacementSettingsId = existing.Id,
            WidgetRef = existing.WidgetRef,
            SettingsJson = existing.SettingsJson,
            SettingsVersion = existing.SettingsVersion,
            SavedUtc = now,
            SavedByUserId = userId,
        });

        existing.WidgetRef = target.WidgetRef;
        existing.SettingsJson = target.SettingsJson;
        existing.SettingsVersion += 1;
        existing.ModifiedUtc = now;
        existing.ModifiedByUserId = userId;

        await db.SaveChangesAsync(ct);
        return true;
    }
}
