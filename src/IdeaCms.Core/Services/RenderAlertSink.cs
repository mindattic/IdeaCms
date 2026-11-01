using Microsoft.Extensions.DependencyInjection;
using IdeaCms.Abstractions;

namespace IdeaCms.Core.Services;

/// <summary>
/// The host <see cref="IRenderAlertSink"/>: when a page resolves a missing/disabled dependency, raise an
/// Admin Inbox alert. Render-path-safe by construction — fire-and-forget on a fresh DI scope (a scoped
/// IAdminInboxService cannot be injected into the singleton render path) and swallow ALL exceptions, so
/// an alert about a problem can never become the render crash it is warning about. The dedup key is NOT
/// keyed on page id/slug, so the same broken reference across 50 pages collapses to ONE inbox row.
/// </summary>
public sealed class RenderAlertSink(IServiceScopeFactory scopeFactory) : IRenderAlertSink
{
    public void RaiseMissing(ContentKind kind, string key, int? version, Guid pageId, string slug) =>
        Fire("Error", "render:missing", "Unresolved content in a page",
             $"A page (e.g. '{slug}') references {Describe(kind, key, version)}, which is not installed.",
             kind, key, version);

    public void RaiseDisabled(ContentKind kind, string key, int? version, Guid pageId, string slug) =>
        Fire("Warning", "render:disabled", "Disabled content used by a page",
             $"A page (e.g. '{slug}') references {Describe(kind, key, version)}, which is currently disabled.",
             kind, key, version);

    private void Fire(string severity, string prefix, string subject, string body, ContentKind kind, string key, int? version)
    {
        var dedup = AdminInboxService.DedupKey(prefix, kind.ToString(), key, version?.ToString() ?? "latest");
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var inbox = scope.ServiceProvider.GetRequiredService<IAdminInboxService>();
                await inbox.RaiseAsync(severity, "Render", subject, body, dedup);
            }
            catch { /* a render-time alert must never escalate into a crash */ }
        });
    }

    private static string Describe(ContentKind kind, string key, int? version) =>
        $"the {kind} '{key}' {(version is int v ? "V" + v : "(latest)")}";
}

/// <summary>No-op sink for tests and the static-SSR fast path.</summary>
public sealed class NullRenderAlertSink : IRenderAlertSink
{
    public void RaiseMissing(ContentKind kind, string key, int? version, Guid pageId, string slug) { }
    public void RaiseDisabled(ContentKind kind, string key, int? version, Guid pageId, string slug) { }
}
