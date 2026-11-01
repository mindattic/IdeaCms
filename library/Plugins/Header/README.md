# Site Header

A 3-column fixed header: logo link on the left, free-form center column, and an authenticated-user dropdown on the right.

---

## Usage

**Token syntax**

```html
<Plugin.Header logo="Acme Corp">nav content here</Plugin.Header>
```

**Blazor tag syntax**

```razor
<IdeaCms.Plugin.Header.V1 Logo="Acme Corp">
    <nav>...</nav>
</IdeaCms.Plugin.Header.V1>
```

---

## Parameters

| Name | Type | Default | Description |
|---|---|---|---|
| `Logo` | `string?` | *(see Notes)* | Logo text displayed as a link to `/`. Falls back through the `header.logo` site setting, then the site key, then `"MindAttic"`. |
| `ChildContent` | `RenderFragment?` | *(empty)* | Content rendered in the center fill column — typically a `<nav>` or search bar. |
| `Class` | `string?` | — | Additional CSS class(es) appended to the `<header>` root element. |
| `Style` | `string?` | — | Inline style string appended to the `<header>` root element's `style` attribute. |
| `Padding` | `string?` | — | CSS padding shorthand applied to the `<header>` root element (e.g. `"0 2rem"`). |
| `Margin` | `string?` | — | CSS margin shorthand applied to the `<header>` root element. |
| `Align` | `string?` | — | Sets `text-align` on the `<header>` root element (e.g. `"center"`). |

---

## Examples

**Minimal — logo from site settings, no center content**

```html
<Plugin.Header></Plugin.Header>
```

**Named logo with navigation links**

```html
<Plugin.Header logo="RocketShip">
  <a href="/features">Features</a>
  <a href="/pricing">Pricing</a>
  <a href="/docs">Docs</a>
</Plugin.Header>
```

**Logo overriding a site-level default**

```razor
<IdeaCms.Plugin.Header.V1 Logo="Dev Preview">
    <span style="font-size:.85rem;color:#f90;">staging environment</span>
</IdeaCms.Plugin.Header.V1>
```

---

## Notes

- **Logo resolution order:** explicit `Logo` parameter > `header.logo` site setting > site key > `"MindAttic"`.
- **User dropdown** is gated by `AuthorizeView`. When no authenticated user is present the right column renders empty — no error or placeholder is shown.
- **Admin link** (`/admin`) appears in the dropdown only when the authenticated user is in the `Admin` role.
- **Sign-out** is a `POST` form to `/_ma-auth/logout` with an `AntiforgeryToken`, making it CSRF-safe. It does not use client-side navigation.
- **User initials** are derived from the user's email or display name: two-part names produce first + last initials; a single token produces up to two characters; an empty name shows `?`.
- **`Padding`, `Margin`, `Align`, and `Style`** are merged into a single `style` attribute on the `<header>` element in that order; `Style` is always last so it can override the others.
- The stylesheet is served from `/_ideas/Plugin/header/1/header.css`. All CSS classes are prefixed `mah-` to avoid collisions with theme or page styles.
- This plugin renders visible markup (a `<header>` element). It is safe to include inline via `<Plugin.Header />` on a single page, or to activate site-wide via the Admin Page Properties plugin list.

## Settings

Per-instance settings (M.IDEA-§4.5), edited in Admin; empty = as designed.

- **Content:** Logo text, Logo link, Admin link text, Sign-out text (not copied by Copy Configuration)
- **Layout:** Padding, Margin, Alignment, Positioning (Fixed / Sticky / Static), Height, Column gap, Stacking order
- **Appearance:** Background, Backdrop filter, Bottom border, Shadow, Logo color / size / weight / letter spacing / opacity / font family, Avatar background / text color / size / corner radius, Menu background / border color / text color / muted color / hover background / corner radius
- **Behavior:** Show user menu, Show email in menu, Show admin link, Logo hover effect, Animations
- **Advanced:** Extra CSS classes, Extra inline style

Values are emitted as `--mah-*` custom properties on the `<header>`; `header.css` reads them with the designed values as fallbacks.
