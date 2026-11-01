# Hero

A full-width hero banner with a headline, optional subtitle, optional call-to-action button, and an optional background image with a darkening overlay to keep text readable.

## Usage

**Token syntax** (inside page content):

```html
<Component.Hero title="Welcome" subtitle="Build something great" imageclass="img-sky" ctatext="Get started" ctahref="/docs" />
```

**HTML tag syntax** (inside a Razor page or component):

```html
<IdeaCms.Component.Hero.V1
    Title="Welcome"
    Subtitle="Build something great"
    ImageClass="img-sky"
    CtaText="Get started"
    CtaHref="/docs" />
```

## Parameters

| Name | Type | Default | Description |
|---|---|---|---|
| `Title` | `string?` | — | The headline text rendered as an `<h1>`. |
| `Subtitle` | `string?` | — | Optional line rendered under the headline as a `<p>`. |
| `ImageClass` | `string?` | — | A page CSS class carrying the base64 `background-image` (the standard page-authoring convention). |
| `Image` | `string?` | — | Direct background image URL or `data:` URI. Alternative to `ImageClass`; both may be combined. |
| `CtaText` | `string?` | — | Call-to-action button label. Rendered only when `CtaHref` is also provided. |
| `CtaHref` | `string?` | — | Call-to-action link target. Rendered only when `CtaText` is also provided. |
| `Align` | `string?` | `center` | Horizontal alignment of inner content. Accepted values: `center`, `left`, `right`. |
| `Class` | `string?` | — | Additional CSS class(es) applied to the outer `<section>`. |
| `Style` | `string?` | — | Inline style string applied to the outer `<section>`. |
| `Padding` | `string?` | — | Padding utility applied to the outer `<section>`. |
| `Margin` | `string?` | — | Margin utility applied to the outer `<section>`. |

## Examples

**Minimal — title only:**

```html
<Component.Hero title="Hello, world" />
```

**Hero with background image class and a CTA:**

```html
<Component.Hero title="Ship faster" subtitle="A Blazor CMS that deploys from a zip file" imageclass="img-dark-gradient" ctatext="Read the docs" ctahref="/docs" align="left" />
```

**Hero with a direct image URL, right-aligned content:**

```html
<Component.Hero title="Our story" image="https://example.com/banner.jpg" align="right" ctatext="Meet the team" ctahref="/about" />
```

## Notes

- **Background image**: two routes are supported. `imageclass=` is the preferred authoring convention — define the `background-image` as a base64 CSS class in the page's own CSS block, then pass the class name here. `image=` accepts any URL or `data:` URI and writes it as an inline `background-image` style directly. When either is present, the `ma-hero-img` CSS class is added automatically, which activates a darkening overlay and forces light text.
- **CTA button**: neither `ctatext` nor `ctahref` alone renders anything. Both must be non-empty for the `<a>` element to appear.
- **Alignment**: any value other than `left` or `right` (including an empty or missing `align=`) falls back to `center`.
- **Single quotes in URLs**: if `image=` contains a `data:` URI with single quotes, they are percent-encoded (`%27`) automatically to keep the CSS `url('…')` value valid.
- **Stylesheet**: the component loads `/_ideas/Component/hero/1/hero.css` via a `<link>` tag. This path is served by the IdeaCms runtime from the `.idea` package.

## Settings (M.IDEA-§4.5)

Every parameter is a per-instance setting in Admin (stored as the tag's attributes). Unset values keep the as-designed look.

- **Content** (not copied by Copy Configuration): `Title`, `Subtitle`, `ImageClass`, `Image`, `CtaText`, `CtaHref`
- **Behavior**: `CtaNewTab`, `Animate` (button hover lift, default on), `Overlay` (image darkening, default on)
- **Layout**: `Align`, `Padding`, `Margin`, `MinHeight`, `MaxWidth` (text column), `Gap`
- **Colors**: `Accent`, `CtaColor`, `Background`, `TextColor`, `TitleColor`, `OverlayColor`
- **Appearance**: `Radius`, `CtaRadius`, `CtaPadding`, `TitleSize`, `SubtitleSize`, `SubtitleOpacity`, `BackgroundSize`, `BackgroundPosition`, `Shadow`
- **Advanced**: `CssClass`, `CssStyle`

Visual values are emitted as `--ma-hero-*` custom properties on the `<section>`; `hero.css` falls back to its original values.
