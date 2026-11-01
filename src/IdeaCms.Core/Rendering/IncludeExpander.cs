using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using IdeaCms.Abstractions;

namespace IdeaCms.Core.Rendering;

/// <summary>
/// Expands free-form author HTML into a render tree, resolving each PascalCase component tag
/// (<c>&lt;Alert /&gt;</c>, <c>&lt;Alert kind="Plugin"&gt;…&lt;/Alert&gt;</c>) to the live content
/// from the catalog — the SAME rendering a compiled page uses via <see cref="IncludeRenderer"/>.
/// Parsing uses AngleSharp tokenization (the shared <see cref="IncludeReferenceParser"/> grammar),
/// so an unresolved/stale/disabled tag degrades to a visible placeholder — never a render crash.
/// When an <see cref="IRenderAlertSink"/> is supplied, a missing/disabled reference also raises an
/// Admin Inbox alert (fire-and-forget). Inner content is passed to the resolved type as
/// <c>ChildContent</c>.
/// </summary>
public static class IncludeExpander
{
    /// <summary>The frozen include-tag prefix. Identity = MindAttic.Ideas.{Kind}.{Name}.V{n}.</summary>
    public const string TagPrefix = "mindattic.ideas.";

    private static readonly HtmlParser Parser = new();

    private sealed class Counter { public int Next; }

    private sealed record ExpandCtx(
        IContentCatalog Catalog, IRawContentGate Gate, ContentTrust Trust,
        IRenderAlertSink? Alerts, Guid PageId, string Slug);

    public static void Expand(
        RenderTreeBuilder builder, ref int seq, string? html,
        IContentCatalog catalog, IRawContentGate gate, ContentTrust trust,
        IRenderAlertSink? alerts = null, Guid pageId = default, string slug = "")
    {
        if (string.IsNullOrWhiteSpace(html)) return;
        // Author-trusted content: upgrade PascalCase HTML tags to <ma-component> before AngleSharp
        // normalises tag names to lowercase. Only Author content may embed component tags — untrusted
        // content leaves PascalCase tags as unknown HTML elements (AngleSharp lowercases them safely).
        if (trust == ContentTrust.Author)
            html = UpgradePascalCaseTags(html);
        // M.IDEA-§4.6: EVERY body is XSS-sanitized (HtmlSanitizer) before expansion; the per-node filters below
        // stay as defense in depth. Author trust keeps the upgraded citizen tags; Untrusted keeps none.
        html = gate.SanitizeBody(html, trust);
        using var doc = Parser.ParseDocument("<!DOCTYPE html><html><head></head><body>" + html + "</body></html>");
        var counter = new Counter { Next = seq };
        RenderNodes(builder, counter, doc.Body!.ChildNodes, new ExpandCtx(catalog, gate, trust, alerts, pageId, slug));
        seq = counter.Next;
    }

    // ── PascalCase component-tag pre-processor ────────────────────────────────────────────────
    // Converts <Alert type="error" /> and <Alert type="error">inner</Alert> to
    // <ma-component data-key="alert" type="error"></ma-component> / <ma-component ...>inner</ma-component>.
    // "ma-component" is a valid HTML5 custom element (hyphen required), so AngleSharp parses it
    // correctly and preserves all attributes and children. The data-key holds the lowercased
    // component key; all other attributes become component parameters in RenderNodes.
    // Order: self-closing first, then open tags, then close tags — all three are idempotent
    // and safe to run in sequence because self-close is consumed before open-tag pass.

    // Require at least two characters with the second being lowercase — prevents matching standard
    // HTML short-tags: <B>, <I>, <P>, <H1>–<H6>, <EM>, <BR>, <HR>, <TR>, <TH>, <TD>, etc.
    // Component/Kind names always have a lowercase second character (Alert, Plugin, Component…).
    private static readonly Regex _pascalSelfClose =
        new(@"<([A-Z][a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)*)((?:\s(?:[^>""'/]|""[^""]*""|'[^']*')*)?)\s*/>", RegexOptions.Compiled);
    private static readonly Regex _pascalOpen =
        new(@"<([A-Z][a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)*)((?:\s(?:[^>""']|""[^""]*""|'[^']*')*)?)>", RegexOptions.Compiled);
    private static readonly Regex _pascalClose =
        new(@"</([A-Z][a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)*)>", RegexOptions.Compiled);

    // Splits "<Kind.Key>" dotted tag names: if the first segment is a valid ContentKind, returns
    // (key, kindStr); otherwise returns the whole lowercased name as the key with no explicit kind.
    private static (string key, string? kind) ParseTagName(string rawName)
    {
        var dotIdx = rawName.IndexOf('.');
        if (dotIdx > 0)
        {
            var kindStr = rawName[..dotIdx];
            if (Enum.TryParse<ContentKind>(kindStr, ignoreCase: true, out _))
                return (rawName[(dotIdx + 1)..].ToLowerInvariant(), kindStr);
        }
        return (rawName.ToLowerInvariant(), null);
    }

    // Exposed internal so IncludeReferenceParser.Parse() can run the same upgrade and then scan
    // ma-component elements for PascalCase-tag component references.
    internal static string UpgradePascalCaseTags(string html)
    {
        // Quick exit: scan for '<' followed by an uppercase letter.
        bool hasPascal = false;
        for (int i = 0; i < html.Length - 1; i++)
            if (html[i] == '<' && char.IsUpper(html[i + 1])) { hasPascal = true; break; }
        if (!hasPascal) return html;

        html = _pascalSelfClose.Replace(html, static m =>
        {
            var (key, kind) = ParseTagName(m.Groups[1].Value);
            var tail = m.Groups[2].Value.Trim();
            var kindPart = kind is not null ? $" kind=\"{kind}\"" : "";
            return tail.Length > 0
                ? $"<ma-component data-key=\"{key}\"{kindPart} {tail}></ma-component>"
                : $"<ma-component data-key=\"{key}\"{kindPart}></ma-component>";
        });

        html = _pascalOpen.Replace(html, static m =>
        {
            var (key, kind) = ParseTagName(m.Groups[1].Value);
            var tail = m.Groups[2].Value.Trim();
            var kindPart = kind is not null ? $" kind=\"{kind}\"" : "";
            return tail.Length > 0
                ? $"<ma-component data-key=\"{key}\"{kindPart} {tail}>"
                : $"<ma-component data-key=\"{key}\"{kindPart}>";
        });

        return _pascalClose.Replace(html, "</ma-component>");
    }

    private static void RenderNodes(RenderTreeBuilder b, Counter c, INodeList nodes, ExpandCtx ctx)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                // Script/style: author content is emitted as-is; untrusted content drops the entire element —
                // an empty <script></script> with attributes (e.g. nonce="…") is still an XSS vector surface.
                case IElement el when el.LocalName is "script" or "style":
                    if (ctx.Trust != ContentTrust.Author) break;   // untrusted: drop the whole element
                    // Whole element as ONE markup frame: a markup child of an element frame is re-parsed
                    // outside raw-text context on interactive render, so "<tag>" text inside the CSS/JS
                    // would become real elements and silently truncate the sheet/script.
                    b.AddMarkupContent(c.Next++, el.OuterHtml);
                    break;

                // PascalCase component tag — <Alert type="error" /> or <Alert>children</Alert> (and nested).
                // UpgradePascalCaseTags() wraps these as <ma-component data-key="…" …> before parsing.
                // An optional kind="Plugin|Component|Theme|Page" attribute on the original tag resolves
                // conflicts when the same key exists in more than one kind; without it, Component is tried
                // first and Plugin is the fallback.
                // AUTHOR TRUST ONLY (M.IDEA-LAW-5). The upgrade pass above already skips untrusted content so
                // that PascalCase tags stay inert HTML — but "ma-component" is a legal custom element, so
                // an untrusted author could hand-write the POST-upgrade shape and bind a live citizen
                // anyway. Gating the case on trust closes that door: for untrusted content the tag falls
                // through to the generic element arm and renders as the inert unknown element it is.
                case IElement el when el.LocalName == "ma-component" && ctx.Trust == ContentTrust.Author:
                {
                    var key = el.GetAttribute("data-key") ?? "";
                    if (string.IsNullOrEmpty(key)) break; // malformed tag — skip silently

                    // Read optional explicit kind; filters out meta-attrs before they reach the component.
                    ContentKind? explicitKind = null;
                    var kindStr = el.GetAttribute("kind");
                    if (kindStr is not null && Enum.TryParse<ContentKind>(kindStr, ignoreCase: true, out var ek))
                        explicitKind = ek;

                    int? version = null;
                    if (int.TryParse(el.GetAttribute("data-version"), out var ver)) version = ver;

                    var attrs = new List<KeyValuePair<string, object?>>();
                    foreach (var attr in el.Attributes)
                    {
                        if (attr.Name is "data-key" or "kind" or "data-version") continue;
                        // Apply the same XSS filtering as regular elements (Author trust bypasses it).
                        if (ctx.Trust != ContentTrust.Author &&
                            (attr.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
                             (UrlAttributes.Contains(attr.Name) && IsUnsafeUri(attr.Value))))
                            continue;
                        attrs.Add(new KeyValuePair<string, object?>(attr.Name, attr.Value));
                    }

                    RenderFragment? childContent = el.ChildNodes.Length > 0
                        ? innerBuilder =>
                        {
                            var ic = new Counter { Next = 0 };
                            RenderNodes(innerBuilder, ic, el.ChildNodes, ctx);
                        }
                        : null;

                    ResolvedContent res;
                    if (explicitKind.HasValue)
                    {
                        res = ctx.Catalog.ResolveTag(explicitKind.Value, key, version);
                    }
                    else
                    {
                        res = ctx.Catalog.ResolveTag(ContentKind.Component, key, version);
                        if (res.Outcome != ContentResolution.Resolved)
                            res = ctx.Catalog.ResolveTag(ContentKind.Plugin, key, version);
                    }

                    if (res.Outcome == ContentResolution.Resolved)
                    {
                        var type   = res.Type!;
                        var pTypes = ParameterTypesOf(type);
                        b.OpenComponent(c.Next++, type);
                        foreach (var attr in attrs)
                        {
                            var val = pTypes.TryGetValue(attr.Key, out var pt)
                                ? CoerceAttributeValue(pt, attr.Value) : attr.Value;
                            b.AddAttribute(c.Next++, attr.Key, val);
                        }
                        if (childContent is not null && HasChildContent(type))
                            b.AddAttribute(c.Next++, "ChildContent", childContent);
                        b.CloseComponent();
                    }
                    else
                    {
                        var displayKey = explicitKind.HasValue ? $"<{explicitKind.Value}.{key}>" : $"<{key}>";
                        EmitMissing(b, ref c.Next, displayKey);
                        if (res.Outcome == ContentResolution.Disabled)
                            ctx.Alerts?.RaiseDisabled(explicitKind ?? ContentKind.Component, key, null, ctx.PageId, ctx.Slug);
                        else
                            ctx.Alerts?.RaiseMissing(explicitKind ?? ContentKind.Component, key, null, ctx.PageId, ctx.Slug);
                    }
                    break;
                }

                case IElement el:
                    b.OpenElement(c.Next++, el.LocalName);
                    foreach (var attr in el.Attributes)
                    {
                        // Strip XSS vectors from untrusted content: event handlers, and unsafe-scheme
                        // URIs on URL-bearing attributes only. data-* custom attributes whose values
                        // start with "data:" (e.g. data-payload="data:…") are safe and must not be
                        // filtered — they are application data, not navigation targets.
                        if (ctx.Trust != ContentTrust.Author &&
                            (attr.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
                             (UrlAttributes.Contains(attr.Name) && IsUnsafeUri(attr.Value))))
                            continue;
                        b.AddAttribute(c.Next++, attr.Name, attr.Value);
                    }
                    RenderNodes(b, c, el.ChildNodes, ctx);
                    b.CloseElement();
                    break;

                case IText t:
                    b.AddContent(c.Next++, t.Text);
                    break;
            }
        }
    }

    /// <summary>
    /// The ONE resolve-and-render routine, shared by the data-page expander (above) and the compiled-page
    /// <see cref="IncludeRenderer"/>/<c>CmsInclude</c> seam — so both produce byte-identical render trees
    /// (attribute flow, ChildContent, Disabled-vs-Missing placeholder, Admin-Inbox alert). Never throws.
    /// </summary>
    internal static void EmitInclude(
        RenderTreeBuilder b, ref int seq,
        ContentKind kind, string key, int? version, string displayTag,
        IContentCatalog catalog,
        IReadOnlyList<KeyValuePair<string, object?>> attributes,
        RenderFragment? childContent,
        IRenderAlertSink? alerts, Guid pageId, string slug)
    {
        var resolved = catalog.ResolveTag(kind, key, version);
        switch (resolved.Outcome)
        {
            case ContentResolution.Resolved:
                var type = resolved.Type!;
                var paramTypes = ParameterTypesOf(type);
                b.OpenComponent(seq++, type);
                foreach (var attr in attributes)
                {
                    // RFC 0001 typed-attribute coercion: a token attribute that matches a declared
                    // typed [Parameter] is converted to that type (bool/int/double/enum/…); anything
                    // else keeps its raw value and lands in the CaptureUnmatchedValues bag.
                    var value = paramTypes.TryGetValue(attr.Key, out var pt)
                        ? CoerceAttributeValue(pt, attr.Value)
                        : attr.Value;
                    b.AddAttribute(seq++, attr.Key, value);
                }
                // Pass inner content as ChildContent only if the resolved type actually declares it.
                if (childContent is not null && HasChildContent(type))
                    b.AddAttribute(seq++, "ChildContent", childContent);
                b.CloseComponent();
                break;

            case ContentResolution.Disabled:
                EmitMissing(b, ref seq, displayTag);
                alerts?.RaiseDisabled(kind, key, version, pageId, slug);
                break;

            default: // Missing
                EmitMissing(b, ref seq, displayTag);
                alerts?.RaiseMissing(kind, key, version, pageId, slug);
                break;
        }
    }

    // Attributes whose values are treated as URLs — IsUnsafeUri is only applied to these.
    // data-* attributes (e.g. data-type="data:application/json") must NOT be filtered here because
    // the "data:" prefix in their VALUE is not a navigation URI; it is safe application data.
    private static readonly HashSet<string> UrlAttributes = new(StringComparer.OrdinalIgnoreCase)
        { "href", "src", "action", "formaction", "data", "poster", "cite", "xlink:href", "background" };

    private static bool IsUnsafeUri(string value)
    {
        var v = value.TrimStart();
        return v.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasChildContent(Type type)
    {
        var p = type.GetProperty("ChildContent", BindingFlags.Public | BindingFlags.Instance);
        return p is not null
            && p.PropertyType == typeof(RenderFragment)
            && p.GetCustomAttribute<ParameterAttribute>() is not null;
    }

    // ---- RFC 0001: typed-attribute coercion -----------------------------------------------------

    // name -> property type for every typed [Parameter] (the CaptureUnmatchedValues bag is excluded
    // on purpose: its values must stay raw). Case-insensitive: token attributes are author markup.
    private static readonly ConcurrentDictionary<Type, Dictionary<string, Type>> ParameterTypeCache = new();

    private static Dictionary<string, Type> ParameterTypesOf(Type componentType) =>
        ParameterTypeCache.GetOrAdd(componentType, static t =>
        {
            var map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanWrite) continue;
                if (p.GetCustomAttribute<ParameterAttribute>() is not { CaptureUnmatchedValues: false }) continue;
                map[p.Name] = p.PropertyType;
            }
            return map;
        });

    /// <summary>
    /// Converts a token attribute's raw value (string, or bool for a bare attribute) to the declared
    /// parameter type: bool, enums (by name, case-insensitive), and the IConvertible numerics
    /// (int/long/double/decimal/…), with Nullable&lt;T&gt; unwrapped. A failed conversion returns the
    /// RAW value — a render never throws; Blazor's own parameter binding then surfaces the mismatch.
    /// </summary>
    public static object? CoerceAttributeValue(Type targetType, object? raw)
    {
        if (raw is null) return null;
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (t.IsInstanceOfType(raw)) return raw;
        if (t == typeof(string)) return raw.ToString();
        if (t == typeof(object)) return raw;
        var s = raw.ToString();
        if (string.IsNullOrWhiteSpace(s)) return raw;
        try
        {
            if (t == typeof(bool)) return bool.Parse(s);
            if (t.IsEnum) return Enum.Parse(t, s, ignoreCase: true);
            return Convert.ChangeType(s, t, CultureInfo.InvariantCulture);
        }
        catch
        {
            return raw;
        }
    }

    internal static void EmitMissing(RenderTreeBuilder b, ref int seq, string tag)
    {
        b.OpenComponent<MissingContent>(seq++);
        b.AddComponentParameter(seq++, nameof(MissingContent.Key), tag);
        b.CloseComponent();
    }
}
