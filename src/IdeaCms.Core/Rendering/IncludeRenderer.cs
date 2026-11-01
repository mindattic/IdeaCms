using Microsoft.AspNetCore.Components;
using IdeaCms.Abstractions;

namespace IdeaCms.Core.Rendering;

/// <summary>
/// Host implementation of the <see cref="IIncludeRenderer"/> SDK seam. Lets a COMPILED Page/Theme drop a
/// citizen by string id (e.g. <c>"IdeaCms.Plugin.Tooltip.V1"</c>) at runtime via the
/// <c>CmsInclude</c> primitive — with the SAME resolution, degradation, and Admin-Inbox alerting as a data
/// page's include tag, because both route through <see cref="IncludeExpander.EmitInclude"/> (one code path,
/// no divergence). Registered in DI and handed to packaged content through
/// <see cref="IRenderContext.TryGetFeature{T}"/>.
/// </summary>
public sealed class IncludeRenderer(IContentCatalog catalog, IRenderAlertSink alerts) : IIncludeRenderer
{
    public RenderFragment Render(
        IRenderContext context, string reference,
        IReadOnlyDictionary<string, object?>? attributes = null, RenderFragment? childContent = null)
        => builder =>
        {
            if (string.IsNullOrWhiteSpace(reference)) return;

            // Lowercase to match the data-page path (AngleSharp lowercases HTML tag names, and catalog keys
            // are stored lowercase) so "IdeaCms.Plugin.Tooltip.V1" resolves identically.
            var localName = reference.Trim().ToLowerInvariant();
            var pageId = context.Page.PageId;
            var slug = context.Page.Slug;

            if (!IncludeReferenceParser.TryParseTag(localName, out var kind, out var key, out var version))
            {
                // Malformed reference — author typo, not a missing dependency, so placeholder without alert.
                var seq0 = 0;
                IncludeExpander.EmitMissing(builder, ref seq0, reference);
                return;
            }

            var attrs = ToAttrList(attributes);
            // A Plugin a compiled Theme/Page composes by string id is still a per-page INSTANCE (M.IDEA-§4.5):
            // its stored slot overrides whatever defaults the composing author passed as attributes.
            if (kind == ContentKind.Plugin && context.GetInstanceSettingsJson(InstanceSlots.Plugin(key)) is { } slotJson
                && catalog.ResolveTag(kind, key, version) is { Outcome: ContentResolution.Resolved, Type: { } pluginType })
            {
                var stored = InstanceSettingsBinder.ToParameters(pluginType, slotJson);
                attrs = attrs.Where(a => !stored.ContainsKey(a.Key))
                    .Concat(stored.Select(s => new KeyValuePair<string, object?>(s.Key, s.Value)))
                    .ToList();
            }
            var seq = 0;
            IncludeExpander.EmitInclude(builder, ref seq, kind, key, version, reference, catalog,
                attrs, childContent, alerts, pageId, slug);
        };

    private static IReadOnlyList<KeyValuePair<string, object?>> ToAttrList(IReadOnlyDictionary<string, object?>? attributes)
    {
        if (attributes is not { Count: > 0 }) return [];
        var list = new List<KeyValuePair<string, object?>>(attributes.Count);
        foreach (var kv in attributes) list.Add(kv);
        return list;
    }
}
