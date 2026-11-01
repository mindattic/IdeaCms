# Social Links

A row of social and profile icon links rendered with inline SVG — no icon files, no web font, no extra requests.

## Usage

**Token syntax (page source):**
```html
<Plugin.SocialLinks github="https://github.com/yourname" email="hi@example.com" />
```

**HTML tag syntax (Razor/Blazor):**
```razor
<IdeaCms.Plugin.SocialLinks.V1 GitHub="https://github.com/yourname" Email="hi@example.com" />
```

## Parameters

| Name | Type | Default | Description |
|---|---|---|---|
| `GitHub` | `string?` | — | GitHub profile URL. Falls back to the `social.github` site setting. |
| `X` | `string?` | — | X (Twitter) profile URL. Falls back to `social.x`. |
| `LinkedIn` | `string?` | — | LinkedIn profile URL. Falls back to `social.linkedin`. |
| `YouTube` | `string?` | — | YouTube channel URL. Falls back to `social.youtube`. |
| `Email` | `string?` | — | Contact email address or `mailto:` URI. Falls back to `social.email`. |
| `Website` | `string?` | — | Personal or company website URL. Falls back to `social.website`. |
| `Rss` | `string?` | — | RSS feed URL. Falls back to `social.rss`. |
| `Class` | `string?` | — | Additional CSS class(es) appended to the outer `<ul>`. |
| `Style` | `string?` | — | Inline style string applied to the outer `<ul>`. |
| `Padding` | `string?` | — | CSS padding shorthand applied to the outer `<ul>` (e.g. `"1rem 0"`). |
| `Margin` | `string?` | — | CSS margin shorthand applied to the outer `<ul>` (e.g. `"2rem auto"`). |
| `Align` | `string?` | — | `text-align` value applied to the outer `<ul>` (e.g. `"center"`). |

## Examples

**Minimal — GitHub and email only:**
```html
<Plugin.SocialLinks github="https://github.com/mindattic" email="hi@mindattic.com" />
```

**Full set, centered:**
```html
<Plugin.SocialLinks
   github="https://github.com/mindattic"
   x="https://x.com/mindattic"
   linkedin="https://linkedin.com/company/mindattic"
   youtube="https://youtube.com/@mindattic"
   email="hi@mindattic.com"
   website="https://mindattic.com"
   rss="https://mindattic.com/feed.rss"
   align="center"
   margin="2rem 0" />
```

**Theme-driven (no inline values) — relies entirely on site settings:**
```html
<Plugin.SocialLinks />
```
When no parameter is supplied, the component reads `social.github`, `social.x`, etc. from the site settings, so a single theme token works across every site that has those settings populated.

## Notes

- **Conditional render:** if no link resolves (neither a parameter nor a matching site setting has a value), the component renders nothing — no empty `<ul>` is emitted.
- **Email normalization:** a bare email address (e.g. `hi@example.com`) is automatically converted to a `mailto:` URI; a value already starting with `mailto:` is used as-is.
- **Site-setting fallback:** each network parameter falls back to a named site setting (`social.github`, `social.x`, `social.linkedin`, `social.youtube`, `social.email`, `social.website`, `social.rss`). An explicit parameter value always wins over the site setting.
- **Icons are inline SVG:** all icon paths are embedded directly in the markup (24×24 viewBox). There are no external icon requests and no dependency on a web font.
- **Link target:** all links open in a new tab (`target="_blank"`) with `rel="noopener noreferrer"`.
- **Safety check:** URLs that fail an internal `IsUnsafeUrl` check are silently omitted.

## Settings

Per-instance settings (M.IDEA-§4.5), edited in Admin; empty = as designed.

- **Content:** GitHub, X / Twitter, LinkedIn, YouTube, Email, Website, RSS, List label (not copied by Copy Configuration)
- **Layout:** Padding, Margin, Alignment (also justifies the row), Gap
- **Appearance:** Button size, Icon size, Corner radius, Icon color, Icon opacity, Hover background, Hover icon color
- **Behavior:** Hover lift, Animations, Open in new tab, Show name tooltips
- **Advanced:** Extra CSS classes, Extra inline style

Values are emitted as `--ma-social-*` custom properties on the `<ul>`; `sociallinks.css` reads them with the designed values as fallbacks.
