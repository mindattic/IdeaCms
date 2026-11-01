using Microsoft.EntityFrameworkCore;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;

namespace IdeaCms.Core.Services;

/// <summary>
/// System admin notifications over <see cref="AdminInboxMessage"/>, deduplicated by
/// <see cref="AdminInboxMessage.DedupKey"/> (its unique index). The canonical "a page tried to render a
/// disabled/missing dependency" alert sink (see RenderAlertSink) upserts through <see cref="RaiseAsync"/>,
/// which is render-path-safe: it NEVER throws (an alert about a problem must not become a new problem).
/// </summary>
public interface IAdminInboxService
{
    /// <summary>Upsert by dedup key: collapse repeats; reopen a Resolved row when the problem recurs. Never throws.</summary>
    Task RaiseAsync(string severity, string category, string subject, string body, string dedupKey, CancellationToken ct = default);
    Task<IReadOnlyList<AdminInboxMessage>> ListAsync(string? status = null, CancellationToken ct = default);
    Task<int> UnreadCountAsync(CancellationToken ct = default);
    Task<bool> MarkReadAsync(int id, CancellationToken ct = default);
    Task<bool> ResolveAsync(int id, CancellationToken ct = default);
}

public sealed class AdminInboxService(IDbContextFactory<CmsDbContext> dbFactory) : IAdminInboxService
{
    /// <summary>Stable dedup key so the same condition collapses to one row regardless of where it fired.</summary>
    public static string DedupKey(params string[] segments) =>
        string.Join(':', segments).ToLowerInvariant();

    public async Task RaiseAsync(string severity, string category, string subject, string body, string dedupKey, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var existing = await db.AdminInbox.FirstOrDefaultAsync(m => m.DedupKey == dedupKey, ct);
            if (existing is not null)
            {
                if (existing.Status is "Resolved" or "Read")
                {
                    // Problem recurred after being resolved or dismissed — reopen so admin sees it again.
                    existing.Status = "New";
                    existing.ResolvedUtc = null;
                    existing.Severity = severity;
                    existing.Category = category;
                    existing.Subject = subject;
                    existing.Body = body;
                    existing.CreatedUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
                else if (existing.Body != body)
                {
                    // Still "New" but the body may reference a different page/slug now — keep it current.
                    existing.Body = body;
                    await db.SaveChangesAsync(ct);
                }
                return; // still "New" -> collapse the repeat (no duplicate row)
            }

            db.AdminInbox.Add(new AdminInboxMessage
            {
                Severity = severity, Category = category, Subject = subject, Body = body,
                DedupKey = dedupKey, Status = "New", CreatedUtc = DateTime.UtcNow,
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Unique-index race: another caller inserted the same dedup key first — benign.
            }
        }
        catch
        {
            // Render-path safety: an inbox alert must never escalate into the crash it is warning about.
        }
    }

    public async Task<IReadOnlyList<AdminInboxMessage>> ListAsync(string? status = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.AdminInbox.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(m => m.Status == status);
        // New first, then most-recent.
        return await q.OrderBy(m => m.Status == "New" ? 0 : 1)
                      .ThenByDescending(m => m.CreatedUtc)
                      .ToListAsync(ct);
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AdminInbox.CountAsync(m => m.Status == "New", ct);
    }

    public async Task<bool> MarkReadAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var m = await db.AdminInbox.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return false;
        if (m.Status == "Resolved") return true;   // already done — idempotent success, not "not found"
        m.Status = "Read";
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ResolveAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var m = await db.AdminInbox.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return false;
        if (m.Status == "Resolved") return true;   // already done — idempotent success
        m.Status = "Resolved";
        m.ResolvedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
