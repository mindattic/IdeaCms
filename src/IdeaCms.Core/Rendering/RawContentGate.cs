using Ganss.Xss;
using Microsoft.AspNetCore.Components;
using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Core.Rendering;

/// <summary>
/// The SOLE place a <see cref="MarkupString"/> is constructed from author content, and the one XSS policy
/// for page HTML (M.IDEA-§4.6, HtmlSanitizer / Ganss.Xss). EVERY trust level is sanitized: no script, event
/// handler, javascript:/data: URL, frame, form or embed survives in page markup. Author trust differs only
/// in what it may additionally keep — citizen tags (<c>&lt;ma-component&gt;</c>, the instance-settings
/// carriers) and sanitized inline style. Deliberate author JavaScript lives solely in the separate,
/// Author-only Page JS field, never in markup.
/// </summary>
public sealed class RawContentGate : IRawContentGate
{
    private readonly HtmlSanitizer _untrusted;
    private readonly HtmlSanitizer _author;

    public RawContentGate()
    {
        _untrusted = CreateStrict();
        _author = CreateAuthor();
    }

    private static HtmlSanitizer CreateAuthor()
    {
        var s = CreateStrict();
        s.AllowedTags.Add("ma-component");
        // A bare <button> (no form can exist) is inert UI a component script may wire up; id anchors links
        // and component hooks. Neither can execute anything on its own.
        s.AllowedTags.Add("button");
        s.AllowedAttributes.Add("id");
        s.AllowedAttributes.Add("type");
        // Links may open a new tab, but never with a live window.opener (the tab-nabbing reason target is
        // stripped for Untrusted): every targeted anchor is forced to rel="noopener noreferrer".
        s.AllowedAttributes.Add("target");
        s.AllowedAttributes.Add("rel");
        s.PostProcessNode += static (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement el && el.HasAttribute("target"))
                el.SetAttribute("rel", "noopener noreferrer");
        };
        s.AllowedAttributes.Add("style");   // CSS-sanitized by HtmlSanitizer (no url(javascript:), expression())
        // Custom properties are how instance settings reach CSS (style="--x:…"); keep them.
        s.RemovingStyle += static (_, e) =>
        {
            if (e.Style.Name.StartsWith("--", StringComparison.Ordinal)) e.Cancel = true;
        };
        // A citizen tag's attributes are its instance settings (arbitrary names, e.g. uid="…", links="…"):
        // keep them, except event handlers and executable-scheme values.
        s.RemovingAttribute += static (_, e) =>
        {
            if (!string.Equals(e.Tag.LocalName, "ma-component", StringComparison.OrdinalIgnoreCase)) return;
            var name = e.Attribute.Name;
            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase)) return;
            if (IsExecutableUrl(e.Attribute.Value)) return;
            e.Cancel = true;
        };
        return s;
    }

    private static HtmlSanitizer CreateStrict()
    {
        var s = new HtmlSanitizer();

        // Form-family tags enable phishing/credential-harvesting overlays.
        foreach (var tag in new[] { "form", "fieldset", "legend", "input", "button",
                                     "select", "textarea", "option", "optgroup", "label", "datalist" })
            s.AllowedTags.Remove(tag);

        // Embedded-content / media tags allow frame injection and plugin execution.
        foreach (var tag in new[] { "video", "audio", "source", "track", "picture",
                                     "embed", "object", "applet", "canvas", "map", "area" })
            s.AllowedTags.Remove(tag);

        // Dangerous attributes:
        //   action/method/enctype/formaction: form hijacking
        //   style: CSS overlay + url() exfiltration (Author re-allows it, CSS-sanitized)
        //   target: tab-nabbing (opener.location re-assignment in the new tab)
        //   usemap/ismap: image-map phishing
        foreach (var attr in new[] { "action", "method", "enctype", "formaction",
                                      "style", "target", "usemap", "ismap" })
            s.AllowedAttributes.Remove(attr);

        // Presentation/semantics hooks every page relies on (HtmlSanitizer strips them by default): class
        // for CSS, role + aria-* for accessibility. None carries script or a URL.
        s.AllowedAttributes.Add("class");
        s.AllowedAttributes.Add("role");
        s.AllowDataAttributes = true;   // data-* is inert application data (tooltips, component hooks)
        s.RemovingAttribute += static (_, e) =>
        {
            if (e.Attribute.Name.StartsWith("aria-", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
        };

        // Restrict URL schemes to safe navigational/contact values only.
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("http");
        s.AllowedSchemes.Add("https");
        s.AllowedSchemes.Add("mailto");
        s.AllowedSchemes.Add("tel");

        // Defense-in-depth: explicitly reject data: URIs at the filter layer.
        // AllowedSchemes already blocks them; this survives any future AllowedSchemes edit.
        s.FilterUrl += static (_, e) =>
        {
            if (e.OriginalUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                e.SanitizedUrl = string.Empty;
        };
        return s;
    }

    private static bool IsExecutableUrl(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var v = new string(value.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).ToArray());
        return v.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }

    public MarkupString Emit(string? html, ContentTrust trust) =>
        string.IsNullOrEmpty(html) ? default : new MarkupString(SanitizeBody(html, trust));

    /// <summary>
    /// Everything the sanitizer would remove, for the save-time page report. Runs a FRESH sanitizer so the
    /// collecting handlers (subscribed last, so they see the author profile's own Cancel decisions) never
    /// touch the shared render-path instances.
    /// </summary>
    public IReadOnlyList<string> Audit(string html, ContentTrust trust)
    {
        if (string.IsNullOrEmpty(html)) return [];
        var s = trust == ContentTrust.Author ? CreateAuthor() : CreateStrict();
        var removed = new List<string>();
        s.RemovingTag += (_, e) => { if (!e.Cancel) removed.Add($"<{e.Tag.LocalName}> element"); };
        s.RemovingAttribute += (_, e) => { if (!e.Cancel) removed.Add($"{e.Attribute.Name}=\"…\" on <{e.Tag.LocalName}>"); };
        s.RemovingStyle += (_, e) => { if (!e.Cancel) removed.Add($"style '{e.Style.Name}' on <{e.Tag.LocalName}>"); };
        s.RemovingAtRule += (_, e) => { if (!e.Cancel) removed.Add("a CSS at-rule"); };
        s.Sanitize(html);
        return removed.GroupBy(r => r).Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ×{g.Count()}").ToList();
    }

    public string SanitizeBody(string html, ContentTrust trust)
    {
        if (string.IsNullOrEmpty(html)) return html;
        return trust == ContentTrust.Author ? _author.Sanitize(html) : _untrusted.Sanitize(html);
    }
}
