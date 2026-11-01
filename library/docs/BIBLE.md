---
codex: 1
project: IdeaCms.Library
code: MAIL
layer: bible
status: living
updated: 2026-10-03
---

# IdeaCms.Library — Project Bible

> Single source of truth for what the first-party library (`library/` in the IdeaCms repo) IS,
> is NOT, and the rules that keep it coherent. The [README](../README.md) says how to build and pack;
> this says how to think about it. Tabular facts live once in
> [`docs/data/components.json`](data/components.json) (L5) and are cited here by `id`. The CMS's own
> canon is `../docs/BIBLE.md` (code MAI).

## 1. The one sentence {#MAIL-§1}

IdeaCms.Library is the **first-party catalog of `.idea` citizens** — Themes, Plugins, and
Components — that ship *with* the IdeaCms CMS, each an independently versioned, independently
packable RCL whose **asset bundle is the single source of truth** for every consumer.

## 2. The product promise {#MAIL-§2}

- **One home, many `.idea`s.** Every first-party Theme/Plugin/Component lives here, each its own small
  project so each `.idea` packs, signs and uploads on its own.
- **The bundle is the interchange format.** A citizen owns its `assets/` (css/js/html/images) once. A raw
  `.html` page can link `assets/*`, a standalone Blazor app can reference the RCL or link `assets/`, and
  the CMS installs the packed `.idea` with the assets bundled into `wwwroot/`.
- **Identity by convention, not configuration.** The key is the namespace tail (lowercased) and the
  version is the `V{n}` class number, so a `.csproj` stays tiny. The shipped catalog is enumerated in
  [`components.json`](data/components.json).
- **Composition by string id.** A citizen pulls in other installed citizens by key via
  `[Uses(ContentKind.…, "key", n)]` + `<CmsInclude Ref="…">`, never by project reference (see
  [theme.cyberspace](data/components.json), [component.frontpage](data/components.json),
  [component.legionpersonas](data/components.json)).
- **Every citizen is configurable per instance.** Its public, simple-typed `[Parameter]`s are its
  instance settings (M.IDEA-§4.5); defaults reproduce the as-designed look, and visual settings reach CSS
  as custom properties with the design value as the `var()` fallback.

## 3. What it is NOT {#MAIL-§3}

- **NOT the CMS host.** The CMS (`../src`) never references anything here; it installs packed `.idea`
  files as optional content. The only thing these projects compile against is the frozen
  `IdeaCms.Abstractions` SDK in `../src`.
- **NOT a `dotnet pack` library.** Every project is `IsPackable=false`; the unit of distribution is the
  `.idea` (a guarded, content-signed zip, [HOUSE-LAW-5](../../../MindAttic.HouseRules.md)). For feed
  transport, `ma-idea nupkg` wraps a signed `.idea` unchanged inside a NuGet package.
- **NOT a place for Pages.** A Page is a CMS database record (Html/Css/Js + tags), not a `.idea`; no page
  source lives here ([MAIL-LAW-8](#MAIL-LAW-8)).
- **NOT a host-assembly bundle.** Abstractions and the framework assemblies it carries are kept out of
  `bin/` (`Private=false` + `ExcludeAssets=runtime`); the validator forbids host assemblies in a packed
  `bin/`.
- **NOT cross-host static web assets.** Chrome lives in a plain `assets/` folder (NOT `wwwroot/`) so the
  Razor SDK never registers it as a static web asset. `assets/` becomes the package `wwwroot/` only at
  pack time.
- **NOT a separate repo.** The library is the `library/` half of the IdeaCms repo, with its own
  solution; the GitHub repo `mindattic/IdeaCms.Library` is archived.

## 4. Architecture canon {#MAIL-§4}

```
   ../src/IdeaCms.* (CMS host)  ── installs packed, signed .idea (optional content) ──┐
        ▲ compiles against (frozen SDK only)                                                   │
        │                                                                                      │
   ../src/IdeaCms.Abstractions ◄── ProjectReference (Private=false, ExcludeAssets=runtime)
        │
   ┌────┴────────────────────────── library/ (IdeaCms.Library.slnx) ─────────────────┐
   │  Directory.Build.props  (net10.0, the ONE Abstractions ref, CopyLocalLockFileAssemblies)   │
   │                                                                                           │
   │   Themes/      ThemeBase     → ThemeCssUrls; chrome + ONE @Body hole; light+dark palette  │
   │   Plugins/     PluginBase    → site-wide behavior; BeforeBody or AfterBody slot           │
   │   Components/  ComponentBase → inline-placed at its <Component.X /> tag; may nest         │
   │   _Shared/     linked-source helpers (e.g. ShadowDomAttach.cs)                            │
   │                                                                                           │
   │   each citizen: assets/ (the bundle) ── tools/pack-all.ps1 [-Sign] ──► dist/*.idea        │
   └───────────────────────────────────────────────────────────────────────────────────────────┘
                     composition: [Uses(kind, "key", n)] + <CmsInclude Ref="…"> (by string id)
```

### 4.1 Projects {#MAIL-§4.1}
The solution ([`IdeaCms.Library.slnx`](../IdeaCms.Library.slnx)) holds **53 citizen
RCLs — 7 Themes, 15 Plugins, 31 Components** — in three solution folders. Two named groups recur in
[`components.json`](data/components.json) notes:
- the **baseline set** — general-purpose parts for ordinary websites (NavMenu, Breadcrumbs, Hero, Card,
  Accordion, Tabs, Gallery, Carousel, Callout, CodeBlock, VideoEmbed, ContactForm, SocialLinks,
  BackToTop, Footer);
- the **mindattic.com verbatim set** — engines extracted verbatim from mindattic.com (TabBoard,
  PinFooter, WebSnapshot).

The full enumeration (key, kind, version, assembly, packed artifact, mount, composition edges) is L5
canon in [`docs/data/components.json`](data/components.json). Common settings and the single
Abstractions reference live once in [`Directory.Build.props`](../Directory.Build.props).

### 4.2 Domain model (NOUNS) {#MAIL-§4.2}
- **Citizen** — a single `.idea`: a Theme, Plugin, or Component. Identity = `key` (namespace tail) +
  `version` (`V{n}` class). Catalogued in [`components.json`](data/components.json).
- **Theme** — chrome (a `theme.css` with light and dark palettes keyed on `html[data-theme-mode]`) plus
  exactly one `@Body` hole; derives from `ThemeBase`, exposes `ThemeCssUrls`; carries the `PagePadding`/
  `PageMargin` settings for its `.page` wrapper. May compose Plugins/Components.
- **Plugin** — a site-wide behavior activator (font loader, effect, tooltip, header, footer); derives from
  `PluginBase`; selected per page in Admin or via the site's `plugins.default`; declares
  `[Idea(Slot = PluginSlot.AfterBody)]` to render after the body (e.g. `plugin.poweredby`).
- **Component** — a parameterized UI element placed inline via its tag; derives from `ComponentBase`,
  typed `[Parameter]` props plus pass-through `Attributes`; may opt in to Shadow DOM.
- **Asset bundle** — a citizen's `assets/` folder; becomes the package `wwwroot/` at pack time and is
  served under the citizen **mount** `/_ideas/<Kind>/<key>/<version>/`.
- **`.idea` artifact** — the packed, content-signed zip in [`dist/`](../dist) (one per citizen), copied
  into the CMS host's `library/`.

### 4.3 Key services (VERBS) {#MAIL-§4.3}
- **build** — `dotnet build -c Release <project>` (or the whole `.slnx`).
- **pack** — `tools/pack-all.ps1` builds every citizen and repacks any whose assembly is newer than its
  `.idea` (`-Force` for all); `-Sign` content-signs each with the Vault `PackageSigning` certificate;
  `-Install` copies them into the host's `library/`. A single citizen packs with
  `dotnet run --project ../src/IdeaCms.Sdk -- pack …`. The packer runs `CitizenValidator` and
  refuses an unsafe citizen.
- **publish** — `tools/publish-nuget.ps1` wraps each signed `.idea` with `ma-idea nupkg` and pushes it
  to the configured feed.
- **compose** — `[Uses(ContentKind, "key", n)]` declares a dependency; `<CmsInclude Ref="…">` renders it;
  resolution is by string id at install/render time.
- **verify** — `ma-idea verify ./dist` checks that every declared dependency resolves.
- **mount/serve** — the CMS serves a citizen's bundle under `/_ideas/<Kind>/<key>/<version>/`. A built
  app bundle can ship as an asset-only Component (no `StylesheetUrls`/`ScriptUrls`) and be served from
  its mount; that route has no default document and no SPA fallback.

## 5. The Laws {#MAIL-§5}

This project **inherits the org-wide House Rules** at
[`MindAttic.HouseRules.md`](../../../MindAttic.HouseRules.md) and the CMS laws in `../docs/BIBLE.md`
(M.IDEA-LAW-1…11) by reference — they are not restated here. The laws below are **library-specific**.

### {#MAIL-LAW-1} The asset bundle is the single source of truth.
A citizen owns its `assets/` (css/js/html/images) once, here, never staged from another repo. No second
copy may exist.

### {#MAIL-LAW-2} Identity is convention, never configuration.
A citizen's **key** is its namespace tail, lowercased; its **version** is the `V{n}` class number. A
`.csproj` declares no key/version. Renaming the tail or class is a breaking identity change.

### {#MAIL-LAW-3} Compose by string id, never by project reference.
The only `ProjectReference` any citizen may carry is the frozen Abstractions SDK. Shared helper source
is linked (`<Compile Include="…_Shared\…" Link="…" />`), not referenced. Dependencies on other citizens
are declared with `[Uses]` and rendered with `<CmsInclude>`.

### {#MAIL-LAW-4} No host assemblies in a packed `bin/`.
Abstractions and the framework assemblies it carries stay out of `bin/` (`Private=false` +
`ExcludeAssets=runtime`). A packed `bin/` holds the citizen's DLL plus any third-party dependencies it
needs (e.g. Markdig for `component.frommd`); host assemblies there are rejected by the validator.

### {#MAIL-LAW-5} Chrome lives in `assets/`, never `wwwroot/`.
Citizen css/js live in a plain `assets/` folder with `StaticWebAssetsEnabled=false`; `assets/` becomes
the package `wwwroot/` only at pack time.

### {#MAIL-LAW-6} Styles are scoped to the citizen.
Every selector is scoped under a citizen-specific root (e.g. `.hello-world`, `.theme-ideas`, `.ma-field`)
so a citizen never leaks styles into the host theme, the page, or a sibling.

### {#MAIL-LAW-7} The CMS never references the library.
The CMS installs packed `.idea`s as optional content and never takes a code dependency on anything here.
The dependency arrow points one way only.

### {#MAIL-LAW-8} Pages are records, not `.idea`s.
Themes, Plugins, and Components ship as `.idea`. A Page is a CMS database record; no page source lives
in the library.

### {#MAIL-LAW-9} Settings default to the design.
An unset instance setting reproduces the citizen exactly as designed; visual settings are emitted as CSS
custom properties only when set, and every `var()` carries the design value as its fallback. A shipped
parameter is never renamed or retyped.

## 6. Verified state {#MAIL-§6}

| Aspect | Status | Evidence |
|---|---|---|
| Packed artifacts present | ✅ | [`dist/`](../dist) holds 53 `*.idea`, one per row in [`components.json`](data/components.json); the same 53 ship in the CMS host's `library/`. |
| Every shipped package passes citizen validation | ✅ | `ShippedContentValidationTests.EveryShippedPackage_PassesCitizenValidation` in `../src/IdeaCms.Tests` (564 passing, 2026-10-03). |
| Install + render of a packed citizen | ✅ | `RenderPipelineTests` in the CMS suite; live renders serve `/_ideas/...` mounts. |
| Library test project | ⬜ | None exists in `library/`; see [RFC 0001](rfc/0001-component-test-harness.md). |
| Raw-HTML consumer demo | 🟡 | Only `Themes/Cyberspace/demo.html` exists. |

## 7. Active frontier {#MAIL-§7}

- [RFC 0001](rfc/0001-component-test-harness.md) — a build/pack/render harness inside `library/`.
- [RFC 0002](rfc/0002-uiux-source-to-idea-pipeline.md) — raw UiUx source → `.idea` generator (planned,
  not started).
- Backlog and acceptance criteria: [`USER_STORIES.md`](USER_STORIES.md).

## 8. Quality bar {#MAIL-§8}

A citizen change is **done** only when:
1. `dotnet build -c Release` of the citizen (and the `.slnx`) is clean — 0 warnings, 0 errors.
2. Its assets live in `assets/` and every selector is scoped to the citizen
   ([MAIL-LAW-5](#MAIL-LAW-5), [MAIL-LAW-6](#MAIL-LAW-6)).
3. Identity is convention-only ([MAIL-LAW-2](#MAIL-LAW-2)); the only `ProjectReference` is Abstractions
   ([MAIL-LAW-3](#MAIL-LAW-3)).
4. It is repacked and signed (`tools/pack-all.ps1 -Sign -Install`) so `dist/` and the host's `library/`
   match source, and the CMS suite's `ShippedContentValidationTests` stay green.
5. Its row in [`components.json`](data/components.json) is current (key/version/kind/uses).
6. For interactive citizens, the live behavior is observed. Mark `✅` only when a build, test or
   observation proves it ([HOUSE-LAW-8](../../../MindAttic.HouseRules.md)).

## 9. Glossary {#MAIL-§9}

- **`.idea`** — a guarded, versioned, content-signed zip ([HOUSE-LAW-5](../../../MindAttic.HouseRules.md));
  the unit of distribution for a citizen.
- **Citizen** — any `.idea` (Theme, Plugin, or Component); one RCL, one `.idea`. Catalog:
  [`components.json`](data/components.json).
- **Theme / Plugin / Component** — see [MAIL-§4.2](#MAIL-§4.2).
- **Baseline set / mindattic.com verbatim set** — see [MAIL-§4.1](#MAIL-§4.1).
- **Asset bundle / `assets/`** — a citizen's css/js/html/images; becomes the package `wwwroot/` at pack
  time.
- **Mount** — the served path `/_ideas/<Kind>/<key>/<version>/` for a citizen's assets.
- **Key** — a citizen's namespace tail, lowercased; its stable string identity.
- **Version** — the `V{n}` class number; whole-number only ([HOUSE-LAW-1](../../../MindAttic.HouseRules.md)).
- **`[Uses]` / `<CmsInclude>`** — declare/render a dependency on another installed citizen by string id.
- **Abstractions** — `IdeaCms.Abstractions`, the frozen SDK every citizen compiles against;
  supplies `ThemeBase` / `PluginBase` / `ComponentBase`, `[Idea]`, `[Uses]`, `[Setting]`, `CmsInclude`.
- **Page record** — a CMS database row; NOT a `.idea` ([MAIL-LAW-8](#MAIL-LAW-8)).
- **RCL** — Razor Class Library, the project type of every citizen (`Microsoft.NET.Sdk.Razor`).
