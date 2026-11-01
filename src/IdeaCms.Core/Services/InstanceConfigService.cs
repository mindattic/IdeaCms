using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Rendering;

namespace IdeaCms.Core.Services;

/// <summary>Why an instance is on the page.</summary>
public enum InstanceSource { Page, Theme, ThemeUses, PagePlugin, SiteDefaultPlugin, BodyTag }

/// <summary>
/// One configurable citizen INSTANCE on a page (M.IDEA-§4.5). <see cref="Path"/> addresses it: <c>page</c>,
/// <c>theme</c>, <c>plugin:{key}</c>, or <c>tag:{index}:{key}</c> for the index-th citizen tag in BodyHtml.
/// </summary>
public sealed record InstanceNode(
    string Path, ContentKind Kind, string Key, int? Version, string DisplayName,
    int Depth, InstanceSource Source, bool Resolved, int SettingCount, int SetCount);

/// <summary>An instance's settings schema plus the values explicitly set on it (unset = default).</summary>
public sealed record InstanceSettingsView(
    int PageId, string PageSlug, string Path, ContentKind Kind, string Key, string DisplayName,
    IReadOnlyList<SettingDescriptor> Schema, IReadOnlyDictionary<string, string?> Values);

public interface IInstanceConfigService
{
    /// <summary>The page's instances in tree order: page, theme, theme-composed plugins, page plugins, body tags.</summary>
    Task<IReadOnlyList<InstanceNode>> GetInstancesAsync(int pageId, CancellationToken ct = default);

    Task<InstanceSettingsView?> GetSettingsAsync(int pageId, string path, CancellationToken ct = default);

    /// <summary>
    /// Replaces the instance's settings with <paramref name="values"/> (a missing/null value = back to the
    /// declared default). Returns null on success, else an error. A tag instance is written into BodyHtml
    /// through the ordinary page save, so trust stamping and page history apply exactly as for a hand edit.
    /// </summary>
    Task<string?> SaveSettingsAsync(int pageId, string path, IReadOnlyDictionary<string, string?> values,
        ClaimsPrincipal user, CancellationToken ct = default);
}

public sealed class InstanceConfigService(
    IDbContextFactory<CmsDbContext> dbFactory,
    IContentCatalog catalog,
    IPageAdminService pages,
    IWidgetInstanceSettingsService slots) : IInstanceConfigService
{
    private sealed record Located(InstanceNode Node, Type? Type, BodyTag? Tag);

    public async Task<IReadOnlyList<InstanceNode>> GetInstancesAsync(int pageId, CancellationToken ct = default) =>
        (await LocateAllAsync(pageId, ct)).Select(l => l.Node).ToList();

    public async Task<InstanceSettingsView?> GetSettingsAsync(int pageId, string path, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var page = await db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pageId, ct);
        if (page is null) return null;
        var located = (await LocateAllAsync(pageId, ct)).FirstOrDefault(l => l.Node.Path == path);
        if (located?.Type is not { } type) return null;

        var schema = SettingsSchema.For(type);
        IReadOnlyDictionary<string, string?> values = located.Tag is { } tag
            ? TagValues(tag, schema)
            : InstanceSettingsBinder.Parse(await SlotJsonAsync(db, pageId, SlotOf(path), ct));
        return new InstanceSettingsView(pageId, page.Slug, path, located.Node.Kind, located.Node.Key,
            located.Node.DisplayName, schema, values);
    }

    public async Task<string?> SaveSettingsAsync(int pageId, string path, IReadOnlyDictionary<string, string?> values,
        ClaimsPrincipal user, CancellationToken ct = default)
    {
        var located = (await LocateAllAsync(pageId, ct)).FirstOrDefault(l => l.Node.Path == path);
        if (located is null) return "That instance is no longer on the page — reload the tree.";
        if (located.Type is not { } type) return "That citizen is not installed, so it has no settings to save.";
        var schema = SettingsSchema.For(type);

        if (located.Tag is { } tag)
        {
            var model = await pages.GetAsync(pageId, ct);
            if (model?.BodyHtml is null) return "Page not found.";
            // Only known settings with values that survive typing are written, so a bad paste cannot
            // inject arbitrary attributes into the author's markup.
            var clean = InstanceSettingsBinder.Parse(InstanceSettingsBinder.Serialize(type, values));
            // ...but an attribute the author already wrote that does not type-check (e.g. an int setting
            // hand-written as "3.5") is left exactly as authored when this save did not change it, rather
            // than silently deleted because some OTHER setting was edited.
            var current = TagValues(tag, schema);
            foreach (var d in schema)
            {
                if (clean.ContainsKey(d.Name)) continue;
                var submitted = values.FirstOrDefault(kv => string.Equals(kv.Key, d.Name, StringComparison.OrdinalIgnoreCase)).Value;
                if (submitted is not null && current.TryGetValue(d.Name, out var was) && was == submitted
                    && tag.Get(d.Name) is { } raw)
                    clean[d.Name] = raw;
            }
            model.BodyHtml = BodyTagIndex.SetSettings(model.BodyHtml, tag.Index, schema.Select(d => d.Name), clean);
            var result = await pages.SaveAsync(model, user, ct);
            return result.Ok ? null : result.Error ?? "Save failed.";
        }

        var json = InstanceSettingsBinder.Serialize(type, values);
        var widgetRef = $"{located.Node.Kind}.{located.Node.Key}" + (located.Node.Version is int v ? "@" + v : "");
        await slots.SaveAsync(pageId, SlotOf(path), widgetRef, json, user.FindFirstValue("ma:uid"), ct);
        return null;
    }

    // ── locating ─────────────────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<Located>> LocateAllAsync(int pageId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var page = await db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pageId, ct);
        if (page is null) return [];
        var site = page.SiteId is int sid ? await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid, ct) : null;
        var storedSlots = await db.WidgetPlacementSettings.AsNoTracking().Where(s => s.PageId == pageId)
            .ToDictionaryAsync(s => s.SlotName, s => s.SettingsJson, StringComparer.OrdinalIgnoreCase, ct);

        var list = new List<Located>();
        Located Slot(string path, ContentKind kind, string key, int? version, Type? type, int depth, InstanceSource src)
        {
            var schema = type is null ? [] : SettingsSchema.For(type);
            var set = InstanceSettingsBinder.Parse(storedSlots.GetValueOrDefault(SlotOf(path))).Count(kv => kv.Value is not null);
            return new Located(new InstanceNode(path, kind, key, version, DisplayNameOf(kind, key, version, type),
                depth, src, type is not null, schema.Count, set), type, null);
        }

        // The page itself (a Code page's compiled type carries settings; a Data page has none).
        Type? pageType = null;
        if (page.Kind == PageKind.Code && page.ComponentTypeName is not null)
        {
            var desc = catalog.All.FirstOrDefault(d => d.Kind == ContentKind.Page
                && string.Equals(d.ClrTypeName, page.ComponentTypeName, StringComparison.Ordinal));
            pageType = desc is null ? null : catalog.ResolveType(desc);
        }
        var pageNode = Slot(InstanceSlots.Page, ContentKind.Page, page.Slug, null, pageType, 0, InstanceSource.Page);
        list.Add(pageNode with { Node = pageNode.Node with { DisplayName = page.Title.Length > 0 ? page.Title : "/" + page.Slug, Resolved = true } });

        // Theme (page override → site default), then the plugins it composes by [Uses].
        var themeKey = page.ThemeKey ?? site?.DefaultThemeKey;
        var themeVersion = page.ThemeVersion ?? site?.DefaultThemeVersion;
        var seenPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(themeKey))
        {
            var res = catalog.ResolveTag(ContentKind.Theme, themeKey!, themeVersion);
            var themeType = res.Outcome == ContentResolution.Resolved ? res.Type : null;
            list.Add(Slot(InstanceSlots.Theme, ContentKind.Theme, themeKey!.ToLowerInvariant(), themeVersion, themeType, 1, InstanceSource.Theme));
            if (themeType is not null)
                foreach (var uses in themeType.GetCustomAttributes(typeof(UsesAttribute), false).Cast<UsesAttribute>()
                             .Where(u => u.Kind == ContentKind.Plugin))
                {
                    if (!seenPlugins.Add(uses.Key)) continue;
                    var pr = catalog.ResolveTag(ContentKind.Plugin, uses.Key, uses.Version == 0 ? null : uses.Version);
                    list.Add(Slot(InstanceSlots.Plugin(uses.Key), ContentKind.Plugin, uses.Key.ToLowerInvariant(),
                        uses.Version == 0 ? null : uses.Version, pr.Outcome == ContentResolution.Resolved ? pr.Type : null,
                        2, InstanceSource.ThemeUses));
                }
        }

        // Page plugins (own selection, else the site's default chrome — same rule as PageHost).
        var pluginRefs = PageAdminService.DeserializePlugins(page.ActivePluginsJson);
        var source = InstanceSource.PagePlugin;
        if (string.IsNullOrWhiteSpace(page.ActivePluginsJson) && page.SiteId is int siteId)
        {
            var def = await db.Settings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Scope == "Site" && s.ScopeId == siteId && s.Key == "plugins.default", ct);
            pluginRefs = PageAdminService.DeserializePlugins(def?.Value);
            source = InstanceSource.SiteDefaultPlugin;
        }
        foreach (var (pk, pkey, pver) in IncludeReferenceParser.ParseUses(pluginRefs))
        {
            if (!seenPlugins.Add(pkey)) continue;
            var pr = catalog.ResolveTag(pk, pkey, pver);
            list.Add(Slot(InstanceSlots.Plugin(pkey), pk, pkey, pver, pr.Outcome == ContentResolution.Resolved ? pr.Type : null, 1, source));
        }

        // Citizen tags in the body — each tag IS an instance, nested as authored.
        if (page.Kind == PageKind.Data)
            foreach (var tag in BodyTagIndex.Scan(page.BodyHtml))
            {
                var (kind, type) = ResolveTag(tag);
                var schema = type is null ? [] : SettingsSchema.For(type);
                list.Add(new Located(new InstanceNode($"tag:{tag.Index}:{tag.Key}", kind, tag.Key, tag.Version,
                    DisplayNameOf(kind, tag.Key, tag.Version, type), 1 + tag.Depth, InstanceSource.BodyTag,
                    type is not null, schema.Count, TagValues(tag, schema).Count), type, tag));
            }
        return list;
    }

    private (ContentKind Kind, Type? Type) ResolveTag(BodyTag tag)
    {
        if (tag.ExplicitKind is { } k)
        {
            var r = catalog.ResolveTag(k, tag.Key, tag.Version);
            return (k, r.Outcome == ContentResolution.Resolved ? r.Type : null);
        }
        var c = catalog.ResolveTag(ContentKind.Component, tag.Key, tag.Version);
        if (c.Outcome == ContentResolution.Resolved) return (ContentKind.Component, c.Type);
        var p = catalog.ResolveTag(ContentKind.Plugin, tag.Key, tag.Version);
        return p.Outcome == ContentResolution.Resolved ? (ContentKind.Plugin, p.Type) : (ContentKind.Component, null);
    }

    private static Dictionary<string, string?> TagValues(BodyTag tag, IReadOnlyList<SettingDescriptor> schema)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in schema)
        {
            var attr = tag.Attributes.FirstOrDefault(a => string.Equals(a.Key, d.Name, StringComparison.OrdinalIgnoreCase));
            if (attr.Key is null || BodyTagIndex.MetaAttributes.Contains(attr.Key)) continue;
            map[d.Name] = attr.Value ?? (d.ValueKind == SettingValueKind.Bool ? "true" : "");
        }
        return map;
    }

    private static string SlotOf(string path) => path;

    private static Task<string?> SlotJsonAsync(CmsDbContext db, int pageId, string slot, CancellationToken ct) =>
        db.WidgetPlacementSettings.AsNoTracking().Where(s => s.PageId == pageId && s.SlotName == slot)
            .Select(s => (string?)s.SettingsJson).FirstOrDefaultAsync(ct);

    private string DisplayNameOf(ContentKind kind, string key, int? version, Type? type)
    {
        var desc = version is int v ? catalog.Find(kind, key, v) : catalog.FindLatest(kind, key);
        var name = desc?.DisplayName is { Length: > 0 } dn ? dn : key;
        return $"{kind}: {name}" + (type is null ? " (not installed)" : "");
    }
}
