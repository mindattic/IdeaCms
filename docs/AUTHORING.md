# Authoring for IdeaCms

Two things make up a site, and they are **different kinds of thing**:

| | **Page** | **Plugin / Component** (Theme / Plugin / Component) |
|---|---|---|
| What it is | content | a reusable capability |
| Where it lives | a **row in the CMS database**, in the page hierarchy | a compiled **`.idea`** package |
| How you make it | admin UI: type Html / Css / Js + citizen tags | author a tiny RCL, `ma-idea pack`, sign, upload |
| How it composes | places citizens by tag (`<Component.X />`) | nests other citizens by tag / `<CmsInclude>` + `[Uses]` |

> **The one rule:** a package never references another package's assembly. Everything composes by **stable
> string id** resolved through the host catalog at runtime. The only shared compile-time dependency is the
> frozen **`IdeaCms.Abstractions`** SDK.

---

## Part A — Authoring a **Page** (no build, no `.idea`)

A page is a database record. You create and edit it entirely in the admin UI:

1. Sign in as an admin → **/admin/pages**.
2. **Add page** (or edit one). Set the slug, title, parent (for the hierarchy), and theme.
3. Fill in the three content sections (Body HTML is a Monaco editor with citizen-tag completion):
   - **Body HTML** — your markup plus citizen tags. Every body is sanitized at every trust level: no
     script, style element, event handler, `javascript:` URL, frame or form survives in markup. An
     Author-trusted (admin) body keeps its citizen tags; an Untrusted body keeps none.
   - **Page CSS** — page-scoped styles (the `page` cascade layer, below `component`).
   - **Page JS** — the only place deliberate author JavaScript lives; emitted only for Author-trusted pages.
4. Save. The page is live at its slug, in the hierarchy, wearing its theme.

A page body composes citizens by tag:

```html
<h1>Contact</h1>
<Plugin.Tooltip />                             <!-- switch on a capability for this page -->
<button data-tooltip="Resolved at runtime">Hover me</button>

<Component.Textbox label="Email" />            <!-- place a component; attributes are its settings -->
<Component.Textbox label="Name" data-version="1" />   <!-- pinned to version 1 -->
```

- **Grammar:** `<Kind.Key [attr="value" …] />`, or paired `<Kind.Key>…</Kind.Key>` to pass children.
  Omit `data-version` to float to the latest enabled version; pin with `data-version="n"` so a later
  upload can't change a page. The page's theme and site-wide plugins are chosen in Page Properties.
- A missing/disabled reference degrades to a **clickable placeholder** (for admins, it opens the uploader
  prefilled with the missing reference) — never a crash.
- Page nesting is the hierarchy (parent/child + sort order); drag-drop reorder in **/admin/pages**.

---

## Part B — Authoring a **Plugin or Component** (a `.idea` capability)

Plugins and Components live in the **[`library/`](../../library)** directory — one home
for every first-party Theme / Plugin / Component, organized `Themes/` `Plugins/` `Components/`. The CMS ships the
packed `dist/*.idea` and seeds them on startup (optional content). Copy an existing component to start a new one.

### The shape

A component is a tiny RCL. Common props + the Abstractions reference come from the repo
`Directory.Build.props`, so the `.csproj` is ~3 lines:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <StaticWebAssetsEnabled>false</StaticWebAssetsEnabled>
  </PropertyGroup>
</Project>
```

**Identity is convention** — no attributes:

| Part | From | Example |
|---|---|---|
| Kind | which base you inherit (`ThemeBase` / `PluginBase` / `ComponentBase`) | `PluginBase` → Plugin |
| Key | the namespace tail after `IdeaCms.<Kind>.` (lowercased) | `…Plugin.Tooltip` → `tooltip` |
| Version | the `V{n}` class name | `V1` → version 1 |

Ship `V2` **alongside** `V1`; never mutate a shipped version.

### Assets: the bundle is the single source of truth

Each component **owns its assets** in a plain **`assets/`** folder (NOT `wwwroot/`, to avoid the Razor
static-web-asset collision). That same bundle serves all three consumers — no duplication:

- **raw `.html` pages** link `assets/*.css` / `*.js` directly (see e.g. `Themes/Cyberspace/demo.html`);
- **standalone Blazor apps** reference the RCL or link the same `assets/`;
- **the CMS** uploads the `.idea`, whose `wwwroot/` *is* that `assets/` folder, served at
  `/_ideas/{Kind}/{key}/{version}/…`.

A code-only Plugin points its asset URLs at that mount:

```csharp
public sealed class V1 : PluginBase
{
    private const string Mount = "/_ideas/Plugin/tooltip/1";
    public override IReadOnlyList<string> StylesheetUrls { get; } = new[] { Mount + "/tooltip.css" };
    public override IReadOnlyList<string> ScriptUrls     { get; } = new[] { Mount + "/tooltip.js" };
}
```

### Composing / nesting Plugins / Components

A Plugin or Component composes others two ways — pick per piece:

- **Compile-in (private):** a sub-component inside the citizen's own assembly (e.g. `PersonaCard` inside
  `LegionPersonas`). Not separately deployed. Use when the piece is only ever used here.
- **Reference-by-id (separately deployed):** `<CmsInclude Ref="IdeaCms.Plugin.SacredGeometry.V1" />`
  in markup **plus** `@attribute [Uses(ContentKind.Plugin, "sacredgeometry", 1)]`. The child ships as its own
  `.idea` and rides along via `uses[]`. Use when the piece is reused or versioned independently.

`[Uses]` → the manifest `uses[]`, which drives: `<head>` asset hoisting, an install-time "missing dependency"
warning, the **delete reference-guard**, and the pre-upload **compose-graph check** (`ma-idea verify`). Nesting
is arbitrary-depth; the page drops only the **top** Plugin or Component's tag.

> Interactive components (typed `[Parameter]`s, `@bind`, `@onclick` — e.g. the LegionPersonas gallery) work when
> stamped on a page: `PageHost` renders the content page in one InteractiveServer circuit, so a stamped component
> is live with **no separate app pool**. Declare it with
> `@attribute [Idea(RenderMode = CmsRenderMode.InteractiveServer)]`.

### Shadow DOM isolation (opt-in)

A Component can render its internal markup inside a real browser Shadow DOM shadow root, so Page/Theme
CSS cannot reach in via ordinary selectors regardless of the cascade-layer order (M.IDEA-§4.4). This is
**opt-in per component** — every existing citizen is unaffected unless it deliberately turns it on.

**The recipe** (see `library/Components/Textbox/V1.razor` for the reference port):

1. Override `protected override bool UseShadowDom => true;` (the base default on `ComponentBase` is `false`).
2. Put `@ref="_root"` on the component's single root element.
3. Add two things to the component's own `.csproj`: a compile-only reference (`Microsoft.AspNetCore.Components.Web`,
   `ExcludeAssets="runtime"` — the same pattern `ModalPopup` already uses for `KeyboardEventArgs`, needed
   here for `IJSRuntime`/`IJSObjectReference`) and a linked-source include of the shared helper:
   `<Compile Include="$(MSBuildThisFileDirectory)..\..\_Shared\ShadowDomAttach.cs" Link="ShadowDomAttach.cs" />`
   (linked, not a project reference — `library/Directory.Build.props` limits every citizen to depending
   on Abstractions alone, and the package validator forbids host assemblies in a packed `bin/`).
4. Call the helper from `OnAfterRenderAsync`, guarded so it only ever runs once, interactively:
   ```csharp
   protected override async Task OnAfterRenderAsync(bool firstRender)
   {
       if (firstRender && UseShadowDom && RendererInfo.IsInteractive)
           await ShadowDomAttach.AttachAsync(JS, _root, CssUrls);
   }
   ```

**The hard authoring rule: the `@ref`'d root's immediate children must be structurally STABLE across
re-renders — no top-level `@if` and no element-type swap directly under the ref.** The attach script
moves the root's already-rendered children into the new shadow root exactly once; Blazor's diffing then
targets the DOM nodes it originally created, and it throws if it ever tries to insert/remove an immediate
child of a root whose parent relationship it moved out from under it. Changes at any DEEPER level (a
grandchild, or the root's own attributes) remain completely safe forever — only the root's immediate
children must be unconditional.

- **Pass:** `Textbox`'s root (`<div class="ma-field">`) always renders the same two children
  (`<input>`, `<label>`) — no `@if`, no swap. Safe to isolate as-is.
- **Fail (would need a refactor first):** `ModalPopup`'s outer element is wrapped in `@if (_open) { }`
  (so the root doesn't exist at all across some renders) and its inner dialog swaps between `<form>` and
  `<div>` depending on `WrapForm` — TWO separate violations of the rule, discovered when attempting to
  port it. Fixing the first (toggle a `hidden` attribute on an always-rendered root instead of an `@if`)
  is mechanical; fixing the second correctly would change real `<form>` submission semantics (native
  Enter-to-submit, autofill, form-associated accessibility), which is a genuine behavior change, not a
  structural one — so `ModalPopup` is **not** shadow-isolated yet. Don't force the opt-in onto a
  component whose root isn't already stable; fix the structure first, or leave it un-isolated.

**Delivering the shadow root's own stylesheet** — two patterns, matching how the component already
serves CSS:
- **Inline `<style>` authors** (most existing citizens): keep the `<style>` block as-is; when the root's
  children move into the shadow root, the `<style>` moves with them, giving free per-instance scoping
  with no other change.
- **`StylesheetUrls`/hardcoded `<link>` authors** (e.g. `Textbox`): keep the light-DOM `<link>`
  unconditional (so a failed/skipped attach still renders fully styled — M.IDEA-LAW-7), and ALSO pass the
  same URL(s) to `ShadowDomAttach.AttachAsync`'s `cssUrls` parameter — the JS module adopts them into the
  shadow root via `adoptedStyleSheets` (falling back to an internal `<link>` if unsupported), fetching
  and parsing each URL once and sharing the result across every shadow root that adopts it.

**Degradation is automatic, not something you write:** the component's `.razor` markup always renders
its complete, correctly-styled light-DOM output first — shadow attachment is a best-effort JS-side
enhancement layered on top, skipped entirely during static prerender (`RendererInfo.IsInteractive ==
false`) and swallowed on any JS/interop failure. A page is never invalid because of this feature.

### Build, pack, verify

From `library/` (the `ma-idea` CLI is `src/IdeaCms.Sdk`):

```pwsh
dotnet build -c Release Plugins/Tooltip
dotnet run --project ../src/IdeaCms.Sdk -- pack `
  --assembly Plugins/Tooltip/bin/Release/net10.0/IdeaCms.Plugin.Tooltip.dll `
  --out ./dist --wwwroot Plugins/Tooltip/assets `
  --refs ../src/IdeaCms.Abstractions/bin/Debug/net10.0

dotnet run --project ../src/IdeaCms.Sdk -- inspect ./dist/IdeaCms.Plugin.Tooltip.V1.idea
dotnet run --project ../src/IdeaCms.Sdk -- verify ./dist   # whole-library compose-graph
```

`tools/pack-all.ps1` builds and repacks every citizen in one pass (`-Sign` content-signs each package
from the Vault `PackageSigning` bucket; `-Install` copies them into the host's `library/`).

`inspect` should show one `bin/` dll (host assemblies excluded) and your `wwwroot/` files; `verify` should
report every declared dependency resolves. The packer refuses a citizen that fails `CitizenValidator`
(unsafe JS/CSS patterns, colliding or reserved setting names).

### Upload

In the CMS admin → **/admin/upload**, drop the signed `.idea`. The host verifies its signature and validates it
(same gate as `ma-idea install`), registers the type, extracts its `wwwroot/`, and it's immediately
referenceable from any page by its tag. An unsigned or tampered package is refused.
Install `V2` later and pinned pages keep `V1` until nothing references it.

### Instance settings (M.IDEA-§4.5)

Every public, writable, simple-typed `[Parameter]` (bool / string / number / enum, nullable allowed) is an
**instance setting**: Admin → Page Management → expand a page → click the instance to edit it, and use
⧉ / 📋 to Copy / Paste Configuration onto another instance of the same citizen on any page. Add
`[Setting]` for the Admin label and grouping:

```csharp
[Parameter, Setting("Corner radius", Group = "Layout", Description = "CSS length")]
public string? Radius { get; set; }                       // null = "as designed" (CSS keeps the default)

[Parameter, Setting("Animate on hover", Group = "Behavior")]
public bool Animate { get; set; } = true;                 // the initializer IS the default Admin shows

[Parameter, Setting("Caption", Group = "Content", Copyable = false)]
public string? Caption { get; set; }                      // content: Copy Configuration skips it
```

Rules: defaults must reproduce the current look exactly; never rename or retype a shipped parameter
(existing pages carry it as a tag attribute); feed CSS through custom properties
(`style="--x:@Radius"` + `var(--x, <default>)`) so the stylesheet stays the default source. A Component's
values are its tag's attributes; a Theme's / Plugin's / Code Page's are per-page slots the host binds.
For client JS, `AddSettingsData(builder, seq, "plugin.key")` (or `data-*` attributes on the root) — read
them live, don't cache at script load. There is intentionally no "all instances of X" setting.

---

## Cheat sheet

| You want to… | Do this |
|---|---|
| Make a page | admin /admin/pages → add → fill Body HTML/CSS/JS + citizen tags |
| Activate a site-wide Plugin | tick it in Page Properties (or `<Plugin.<Key> />` in the body for one page) |
| Place an inline Component | `<Component.<Key> attr="…" />` |
| Pick a theme | the Theme dropdown in Page Properties |
| Nest a citizen inside a citizen | `<CmsInclude Ref="…"/>` + `[Uses(...)]` (or a private sub-component) |
| Float vs pin a version | omit `data-version` to float; `data-version="n"` to pin |
| Ship a new version | add a `V{n+1}` class; never edit `V{n}` |
| Author a new Plugin or Component | copy a folder in `library/Plugins/` or `library/Components/`, build, `ma-idea pack`, `ma-idea sign`, upload |
