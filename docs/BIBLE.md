---
codex: 1
project: IdeaCms
code: M.IDEA
layer: bible
status: living
updated: 2026-10-03
---

# IdeaCms — Project Bible

> Single source of truth for what IdeaCms IS, is NOT, and the rules that keep it coherent.
> README.md says how to build/run; this says how to think about the system. Pending decisions that
> cannot be folded in yet would sit in [`AMENDMENTS.md`](AMENDMENTS.md) (normally empty).

## 1. The one sentence {#M.IDEA-§1}

IdeaCms is a **single-deployment Blazor CMS** — one App Service, one app pool, one
database — that hosts *many* pages and goes live the moment you upload a `.idea` zip, with **no
redeploy and no app-pool restart**.

## 2. The product promise {#M.IDEA-§2}

You ship capability by **uploading or CLI'ing a `.idea` file** (a plain, content-signed zip). The CMS
reads whether it contains a **Page**, **Plugin**, **Component**, or **Theme**, registers it by
convention, and it is live. The four kinds derive from one shared root `IdeaBase`.

- **A Page is free-form. A Theme wraps it. Plugins activate site-wide behaviors. Components drop into
  specific positions.** There are no zones, panes, slots, or grids (DotNetNuke's fixed-layout model is
  rejected).
- A **Plugin** is site-wide: selected per page in Admin → Page Properties (or inherited from the site's
  `plugins.default`), it activates across the whole rendered page (e.g. Tooltip adds global tooltip
  behavior; OutfitFont loads a font family). A Plugin declares whether it renders before or after the
  body ([§4.4](#M.IDEA-§4.4)). It may also be placed inline with `<Plugin.X />` on a one-off page.
- A **Component** is lexically scoped: it renders at the exact `<Component.X />` tag position in the
  page body. Components nest: a paired tag passes its inner markup to the outer component as
  `ChildContent`.
- **Two authoring paths, one render path.** A *Data page* (free-form `BodyHtml`/`PageCss`/`PageJs`
  in the DB, zero deploy — the primary path) and a *Code page* (a compiled `PageBase` subclass for
  genuine Blazor interactivity). A page can graduate Data ↔ Code as a **row edit**, never a schema
  change.
- **Every instance is configurable.** A citizen's public, simple-typed `[Parameter]`s are its instance
  settings, edited per instance in Admin and shared only by copy/paste ([§4.5](#M.IDEA-§4.5)).
- **Never change, only enhance.** Versions are whole numbers (`V1`, `V2`); you ship `V2` *alongside*
  `V1` and never mutate a shipped version. Coexisting versions are the never-break mechanism.
- **A page must never be invalid.** A missing/disabled reference degrades to a visible placeholder
  and fires an Admin Inbox alert — never a crash.

## 3. What it is NOT {#M.IDEA-§3}

- **NOT a zone/pane/slot/grid layout engine.** Composition is *lexical* — the author places tags in
  free-form markup.
- **NOT SemVer.** No `1.5.11` anywhere; whole-number versions only
  ([HOUSE-LAW-1](../../MindAttic.HouseRules.md#HOUSE-LAW-1)). NuGet transport uses `{n}.0.0` only
  because NuGet requires it; the CMS identity stays the integer.
- **NOT one-web-app-per-page.** A whole standalone frontend collapses into a single Page (mindattic.com
  became the `frontpage` Data page; MindAttic.Legion.Frontend became the one-tag `personas` page), not
  its own Site. Sites are for genuinely separate domains ([§4.10](#M.IDEA-§4.10)).
- **NOT a per-page router.** Pages resolve by `(SiteId, Slug)` data lookup through one catch-all
  `PageHost`, never per-page routing — so a runtime-loaded `.idea` type renders with zero router
  changes.
- **NOT the owner of sign-in.** Authentication is the MindAttic.Authentication package (6.0.0,
  which also owns auth email and security alerts: SMTP from `MindAttic:Vault:Notifications:email`, else a
  startup warning). Ideas hosts the reset pages (`/forgot-password`, `/account/reset`) and sets
  `MindAttic:Auth:Reset:PublicBaseUrl` per environment (story M.IDEA-US-F10)
  ([HOUSE-LAW-7](../../MindAttic.HouseRules.md#HOUSE-LAW-7)). What stays Ideas-owned is the Admin role,
  the `Cms.AuthorRawMarkup` claim, and the raw-content trust gate.
- **NOT hard-delete by default.** Disable = exists-but-unusable; a version-specific delete is
  reference-guarded ([HOUSE-LAW-2](../../MindAttic.HouseRules.md#HOUSE-LAW-2)).
- **NOT WebAssembly for uploaded packages.** Uploaded `.idea` types are Static or InteractiveServer
  only (a hard .NET boundary).
- **NOT a brace-token language.** `{{ … }}` is not part of the composition grammar; the tag form is
  ([§4.4](#M.IDEA-§4.4)).
- **NOT a sandbox or showroom host.** Nothing in the product resets or deletes a site's content on a
  timer. A demo is a separate, vanilla deployment with its own database ([M.IDEA-LAW-11](#M.IDEA-LAW-11)).
- **NOT a by-type settings layer.** There is no "settings for every instance of citizen X"; settings
  belong to one instance ([§4.5](#M.IDEA-§4.5)).
- **"Idea" is NOT a content kind.** It names the shared base `IdeaBase`, the `.idea` package, and
  the `/_ideas/...` asset route — never one of the content kinds.

## 4. Architecture canon {#M.IDEA-§4}

```
                upload / CLI a .idea (content-signed zip; also fetched from a NuGet feed)
                          │
                          ▼
   ┌──────────────────────────────────────────────────────────────────────────┐
   │  IdeaCms.Blazor   (Blazor Web App, global InteractiveServer)        │
   │   PageHost  /{*slug} ─► ISiteResolver(host) ─► (SiteId,Slug) ─► Theme ─► render
   │   CmsHead (@layer global, theme, page, component)                           │
   │   /_ideas/{Kind}/{key}/{ver}/…  /_media/{uid}  /_health  /_demo/reveal      │
   │   /admin (Admin policy)   MindAttic.Authentication · Vault · Legion · Media  │
   │   CLI verbs: --export/--import/--compose-idealist, --extract/--upload-media │
   └───────────┬──────────────────────────────────────┬──────────────────────────┘
               │ uses                                  │ uses
   ┌───────────▼────────────────┐         ┌────────────▼──────────────────────────┐
   │ IdeaCms.Core        │         │ IdeaCms.Packaging              │
   │  CmsDbContext (EF, SQL,     │         │  manifest kernel + reader + validator  │
   │   temporal Pages, auth      │         │  + CitizenValidator + packer + signer  │
   │   schema)                   │         │  + version resolver (pure, IO-free)    │
   │  catalog · install · ALC    │         └────────────┬──────────────────────────┘
   │  IncludeExpander · gate     │                      │
   │  instance settings · sites  │         ┌────────────▼──────────────────────────┐
   │  .idealist import/export    │         │ IdeaCms.Sdk  (ma-idea CLI)     │
   └───────────┬────────────────┘         │  pack/inspect/list/install/verify/     │
               │ references                │  sign/nupkg                            │
   ┌───────────▼────────────────────────────────────────────────────────────────┐
   │ IdeaCms.Abstractions   (frozen v1 SDK, MAJOR pinned at 1)            │
   │  IdeaBase + PageBase/PluginBase/ThemeBase/ComponentBase, [Idea] [Uses]       │
   │  [Setting], IRenderContext, IPageTree, IComponentMetadataStore, IDemoAccess, │
   │  ICmsContentSource/ITypeResolver/IRawContentGate seams                       │
   │  refs ONLY Microsoft.AspNetCore.Components + System.Text.Json                │
   └────────────────────────────────────────────────────────────────────────────┘
   First-party Themes/Plugins/Components live in library/ (its own solution and canon,
   library/docs/BIBLE.md), packed to library/dist/*.idea and copied into the host's library/.
```

### 4.1 Projects {#M.IDEA-§4.1}
- **`src/IdeaCms.Abstractions`** — the frozen v1 SDK: `IdeaBase` + the four kind bases
  (`PageBase`/`PluginBase`/`ThemeBase`/`ComponentBase`), `[Idea]` (incl. `Slot`), `[Uses]`,
  `[Setting]`, `IRenderContext`, `IPageTree`, `IComponentMetadataStore`, `IDemoAccess`, `CmsInclude`,
  discovery/catalog seams. References ONLY `Microsoft.AspNetCore.Components` + `System.Text.Json`.
- **`src/IdeaCms.Core`** — EF entities, `CmsDbContext` (SQL Server, temporal `Pages`, the
  authentication package's isolated `auth` schema), convention discovery, persisted catalog, package
  install, raw-content gate, `FreeFormPage`/include expander, instance settings, site resolution,
  `.idealist` import/export, the collectible-ALC type resolver, seed.
- **`src/IdeaCms.Packaging`** — the pure `.idea` wire contract: manifest kernel, reflection-only
  packer, zip-slip-guarded reader, manifest validator (incl. host-assembly `bin/` audit and retired
  categories), `CitizenValidator`, `PackageSigner`, SHA-256, version resolver.
- **`src/IdeaCms.Rendering`** — `PageHost` (the catch-all) and `CmsHead` (the cascade).
- **`src/IdeaCms.Sdk`** — the `ma-idea` CLI (pack / inspect / list / install / upgrade / verify /
  sign / nupkg).
- **`src/IdeaCms.Blazor`** — the Blazor Web App host: `/admin`, `/_ideas`, `/_media`,
  `/_health`, `/_demo/reveal`, the CLI verbs, `BootProvisioning`, Vault/Legion/Authentication/Media
  wiring.
- **`src/IdeaCms.Tests`** — the NUnit suite.
- **`library/`** — the first-party citizen library (`library/IdeaCms.Library.slnx`); builds
  independently of the CMS and references only `src/IdeaCms.Abstractions`
  (`Private=false`, `ExcludeAssets=runtime`). Its own canon is `library/docs/BIBLE.md` (code MAIL).
- **`infra/`** — Bicep for the Azure estate; **`.github/workflows/`** — `azure-deploy.yml` and
  `demo-reset.yml` ([§4.14](#M.IDEA-§4.14)); **`seed/`** — the company site's `.idealist` and the demo's
  front page.

### 4.2 Domain model — the NOUNS {#M.IDEA-§4.2}
- **`IdeaBase`** — shared root of all content kinds.
- **`ContentKind`** — `Page=0 · Plugin=1 · Theme=2 · Component=4`. Ordinal 3 is reserved and never
  reused ([M.IDEA-LAW-2](#M.IDEA-LAW-2)).
- **`Page`** (`src/IdeaCms.Core/Entities/Page.cs`) — one durable EF row, `PageKind {Data,Code}`;
  Data columns `BodyHtml`/`PageCss`/`PageJs`/`BodyTrust`; shared `SiteId`/`ParentId`/`Slug`/`ThemeKey`/
  `ThemeVersion`; `SeoMetaJson` (`{title,description}`); `ActivePluginsJson` (array of
  `"Plugin.key[@n]"`; null = inherit the site's `plugins.default`, `[]` = none); `WorkflowDefinitionId`/
  `WorkflowState`. System-versioned temporal table.
- **`Site`** — a tenant: `Key`, `HostBindings`, `IsDefault`, `DefaultThemeKey`/`DefaultThemeVersion`,
  `DefaultThemeMode` (`light`|`dark`).
- **`Setting`** — Host- or Site-scoped key/value (`page.frontpage`, `plugins.default`, `GlobalCss`,
  `nav.*`, `contact.action`, `social.*`).
- **`CmsContentDefinition`** — the persisted catalog row: `UNIQUE(Kind,Key,Version,Origin)`, `Priority`,
  `IsShadowed`, asset mount, raw bundle.
- **`InstalledPackage`** — `.idea` registry row: `(Category,Key,Version)` unique, verbatim manifest,
  blob path, content hash (`Sha256`), `Enabled`.
- **`WidgetPlacementSettings`** (+ `…History`) — a page's tagless instance-settings slots (`theme`,
  `page`, `plugin:{key}`), versioned with rollback.
- **`PageSlugHistory`** — old and vanity slugs that 301 to the page's current slug.
- **`WorkflowDefinition` / `WorkflowTransitionDef`** — named page state machines with role-gated
  transitions.
- **`ComponentMetadata`** — per-page, per-component JSON slots (e.g. the `frommd` Markdown a project
  page renders).
- **`AdminInboxMessage`** — dedup-by-`DedupKey`, severity/status, reopen-on-recurrence.
- **Media items** — MindAttic.Media rows addressed by uid, served at `/_media/{uid}` ([§4.11](#M.IDEA-§4.11)).
- Identity is the triple **`(ContentKind Kind, string Key, int Version)`** ([M.IDEA-LAW-1](#M.IDEA-LAW-1)).

### 4.3 Key services — the VERBS {#M.IDEA-§4.3}
- **`CompiledContentSource` / `DiscoveryService`** (`Core/Discovery`) — convention discovery of compiled
  citizens into the catalog.
- **`ContentCatalog`** (`Core/Discovery`) — the one catalog; ordered `ICmsContentSource` providers feed it;
  key lookups are case-insensitive.
- **`AlcAwareTypeResolver` / `CmsPackageLoadContext`** (`Core/Discovery`) — load `.idea` citizens through
  a per-package collectible `AssemblyLoadContext`; host types unify by reference identity.
- **`IncludeExpander` / `IncludeReferenceParser`** (`Core/Rendering`) — sanitize the body, then resolve
  citizen tags against the catalog; unresolved/disabled → placeholder linking to
  `/admin/upload?missing=<reference>`.
- **`RawContentGate`** (`Core/Rendering`) — the sole `MarkupString` chokepoint and the HtmlSanitizer
  policy ([§4.6](#M.IDEA-§4.6)).
- **`PageAssetCollector`** (`Core/Rendering`) — cascade-orders/dedupes a page's citizen css/scripts into
  `<head>`.
- **`PackageInstallService`** (`Core/Services`) — verify content signature → validate → check
  `requires[]`/`minHostVersion` → register `InstalledPackage` + mirrored catalog row; idempotent,
  hash-conflict-rejecting, soft-disable, reload catalog ([§4.8](#M.IDEA-§4.8)).
- **`ContentLifecycleService`** (`Core/Services`) — enable/disable + reference-guarded version-specific
  delete (body tags, `uses[]`, and `ActivePluginsJson` all count as references).
- **`AdminInboxService` / `RenderAlertSink`** (`Core/Services`) — DB-backed dedup alerting; the render
  thread fire-and-forgets and never throws.
- **`PageAdminService` / `PageAuthoring`** (`Core/Services`) — page CRUD, soft-delete, publish, trust
  stamping, SEO metadata, slug-history writes, effective-plugin computation, Untrusted `PageCss`
  normalization.
- **`PageMarkupValidator`** (`Core/Services`) — save-time page validation ([§4.6](#M.IDEA-§4.6)).
- **`InstanceConfigService` / `BodyTagIndex`** — enumerate and edit a page's citizen instances
  ([§4.5](#M.IDEA-§4.5)).
- **`SlugRedirectService`**, **`WorkflowService`**, **`WidgetInstanceSettingsService`**,
  **`PageHistoryService`** — [§4.12](#M.IDEA-§4.12).
- **`SiteResolver` / `SiteAdminService`** (`Core/Sites`, `Core/Services`) — [§4.10](#M.IDEA-§4.10).
- **`IdeaListImporter` / `IdeaListExporter`** (`Core/Portability`) — [§4.9](#M.IDEA-§4.9).
- **`SeedService`** (`Core/Services`) — idempotent upsert-by-key seed that never clobbers admin edits;
  converts any stored brace token to the tag form at startup; with an idealist configured, seeds only
  the structural minimum.
- **`CssConflictMerger` / `CssShorthandLinter`** (`Core/Services`) — [§4.7](#M.IDEA-§4.7).
- **Authentication** — `AddMindAtticAuthentication<CmsDbContext>`, `UseMindAtticAuthentication()`,
  `MapMindAtticAuthEndpoints()`; `IdeaCmsClaimsAugmentor` adds the Admin role's `Cms.AuthorRawMarkup`
  claim. The first admin is created from the Vault bootstrap token; Admin MFA is currently not required
  (`MindAttic:Auth:Mfa:RequireForAdmin=false`).

### 4.4 Composition grammar {#M.IDEA-§4.4}
In a Data page body, a citizen is placed with a PascalCase HTML tag:

```html
<Component.Textbox />
<Plugin.Tooltip />
<Component.TabBoard alwaysShowTabPage="true" />
<Component.TabBoard data-version="2" />          <!-- pin a version; omit to float to latest -->
<Component.Outer><Component.Inner /></Component.Outer>   <!-- inner becomes ChildContent -->
```

- The kind segment is optional: `<Textbox />` resolves as Component, then Plugin. An explicit
  `kind="Plugin"` attribute also selects the kind. Unknown kinds never become Page references.
- A floating tag resolves the **highest enabled version**; `data-version="n"` pins. A pinned version
  cannot be deleted while a page pins it; a floating reference blocks deletion only when it would orphan.
- Other attributes are instance settings: a value matching a typed `[Parameter]` coerces to
  bool/int/double/decimal/enum (nullable unwrapped); a failed conversion falls back to the raw value;
  unmatched attributes land in the `CaptureUnmatchedValues` bag. One shared `EmitInclude` path serves
  data pages and `<CmsInclude>` in code pages, so both render identically.
- Compiled citizens declare dependencies with `[Uses(ContentKind, "key", n)]`; packages list them in
  `uses[]` as `Kind.key[@n]` (advisory: a miss raises an inbox alert at render) and `requires[]` (a hard
  install-time gate).
- **Plugin slots.** `[Idea(Slot = PluginSlot.AfterBody)]` renders a plugin after the body; the default is
  `BeforeBody`. `PageHost` partitions active plugins by slot, keeping author order within each, and
  renders both passes *inside* the theme's `Body` hole so the theme's `.page` wrapper frames header,
  breadcrumbs and footer too.
- **Brace tokens** are not grammar. `SeedService` converts any `{{ … }}` still stored in a body into tags
  (parameters and versions kept), so old content never renders as visible text.
- **Shadow DOM** is opt-in per Component (`UseShadowDom`); see `docs/AUTHORING.md`.

### 4.5 Instance settings {#M.IDEA-§4.5}
- A citizen's **instance settings are its public, writable, simple-typed `[Parameter]`s** (bool, string,
  integer, floating, enum; nullable allowed). `[Setting]` adds the Admin label, group, order, help text,
  `Multiline`, `Hidden`, and `Copyable = false` for content (captions, links, image uids). A bool
  setting is how a citizen exposes an on/off feature.
- A **Component** (or inline Plugin) instance *is* its tag, so its settings are that tag's attributes;
  Admin edits rewrite exactly that tag's attribute list (`BodyTagIndex`) and save through the normal page
  save (trust stamping and history apply).
- A **Theme**, a page/site-default **Plugin**, a Plugin a compiled Theme/Page composes by string id, and
  a **Code page** have no tag; their settings live in the page's `WidgetPlacementSettings` slots
  (`theme`, `page`, `plugin:{key}`). `PageHost` binds each slot onto the citizen; `IncludeRenderer`
  merges `plugin:{key}` over the composing author's attributes; `IRenderContext.GetInstanceSettingsJson`
  exposes them.
- Unknown names and unparsable values are dropped on write and on bind.
- **No by-type layer.** Configuration is shared by Admin **Copy / Paste Configuration** between two
  instances of the same citizen; only `Copyable` settings travel, and a paste writes nothing until Save.
- Admin's page tree expands each page into its instances (Page, Theme, theme-composed plugins, page
  plugins or the site default, nested body tags), each with Edit / Copy / Paste. A signed-in Admin sees
  a hover-revealed cog on every content page that deep-links to `/admin/pages?page={id}`.
- `IdeaBase.AddSettingsData` emits `<data hidden data-ma-settings="{kind.key}" value="{json}">` so a
  citizen's script can read its settings live across in-circuit navigation.
- `ThemeBase` carries `PagePadding` (default `.75rem 1rem`) and `PageMargin` for the `.page` wrapper.

### 4.6 Trust, sanitization and validation {#M.IDEA-§4.6}
- Trust is stamped at write time ([M.IDEA-LAW-5](#M.IDEA-LAW-5)). **Every page body, at every trust level, is
  run through HtmlSanitizer** (`IRawContentGate.SanitizeBody`) before `IncludeExpander` builds a render
  tree; the expander's own per-node filter stays behind it as defense in depth.
- Neither profile keeps `<script>`, event handlers, `javascript:`/`vbscript:`/`data:` URLs, frames, forms,
  embeds or `<style>` in markup. Both keep `class`, `role`, `aria-*`, `data-*`. **Author** trust
  additionally keeps citizen tags and their settings (minus handlers and executable-scheme values),
  `id`, a form-less `<button>`, `target` (forced `rel="noopener noreferrer"`), and inline `style` through
  HtmlSanitizer's CSS sanitizer. **Untrusted** bodies keep no citizen tags.
- Deliberate author JavaScript lives only in the Author-only **Page JS** field, never in markup.
- `<style>`/`<script>` raw text is emitted as one markup frame, so `<tag>`-looking text inside it
  survives interactive re-render.
- **Validation, one rule set in three places.** *Page save* — `PageMarkupValidator`: every citizen tag
  resolves (else Error), every attribute is a declared setting or pass-through (else Warning), typed
  values fit (else Error), an Untrusted page's tags are flagged as non-rendering, and
  `IRawContentGate.Audit` lists what the sanitizer would remove. *Library pack* — `CitizenValidator`
  (called by `Packer.Pack`; a failing citizen produces no package): colliding or reserved setting names,
  JS with `eval(`/`new Function(`/`document.write(`/string `setTimeout`/`setInterval`, CSS with
  `expression(`/`javascript:`/`-moz-binding`/`behavior:`. *CI* — every package in the host's committed
  `library/` passes the pack checks and every page of `seed/mindattic-site.idealist` validates with no
  Error and nothing sanitizer-stripped.

### 4.7 CSS cascade {#M.IDEA-§4.7}
- `CmsHead` emits `@layer global, theme, page, component;` once, before any CSS, and wraps each tier in
  its named layer (inline text via `@layer x { … }`, external stylesheets via `@import url(…) layer(x);`).
  Layer precedence decides between tiers before specificity, so no tier needs `!important` to beat a
  lower one; inline `style=""` and `!important` stay above the layered cascade ([M.IDEA-LAW-4](#M.IDEA-LAW-4)).
- **Component beats Page:** a Component guarantees its own presentation whatever page it sits in.
- `CssConflictMerger` collapses exact same-selector duplicate rule blocks within one CSS text, keeping
  every byte outside a merged group untouched and refusing any group whose shorthand uses `var()` or
  whose properties it cannot round-trip (e.g. vendor-prefixed). It runs on **Untrusted** `PageCss` at save
  and on library CSS via `--expand-css-shorthand`; Author `PageCss` is stored verbatim and the shared
  `GlobalCss` is never rewritten.
- `CssShorthandLinter` is a non-blocking save-time advisory for a shorthand co-occurring with some but not
  all of its longhands.

### 4.8 Packages: install, signing, distribution {#M.IDEA-§4.8}
- **Content signature.** Every `.idea` carries `idea.sig.json` (`{algorithm, signerThumbprint, signature}`):
  RSA-2048 RSASSA-PSS/SHA-256 over a canonical manifest of every entry's path + SHA-256. It is verified at
  the one choke point every install path shares (`PackageInstallService.InstallAsync`), before the
  manifest is trusted. The verifying key comes from the host's trust (`VaultPackageSigningTrust`, Vault
  bucket `PackageSigning`, `signing-cert-public`), never from the archive; the thumbprint is only a
  diagnostic. Failures throw `PackageSignatureException` (`NotSigned`/`Malformed`/`UntrustedSigner`/
  `BadSignature`). Unsigned packages are refused.
- **Content hash.** `InstalledPackage.Sha256` is the hash of the canonical manifest, so re-signing
  unchanged content is a no-op while any content change is detected. Same version + different content
  is `HashConflict`: nothing is written and an Admin Inbox alert is raised, unless `allowOverride`.
- **Dependencies.** `minHostVersion` blocks install on an older host (`MIN_HOST_VERSION_UNMET`);
  `requires[]` must all be present and enabled before any byte is persisted (`REQUIRES_UNMET`).
- **Collisions.** An upload whose `(Kind,Key)` collides with a compiled citizen is refused unless an
  admin confirms the override.
- **Distribution.** Each citizen is also an independent NuGet package, id `IdeaCms.{Category}.{Key}`,
  version `{n}.0.0`, carrying the signed `.idea` unchanged at `content/{key}.idea`. Publishing is
  `library/tools/pack-all.ps1 -Sign` → `ma-idea nupkg` → `library/tools/publish-nuget.ps1`.

### 4.9 Portability: `.idealist` and boot provisioning {#M.IDEA-§4.9}
- A `.idea` moves a citizen; a **`.idealist`** (zip: `idealist.json` + `media/`) moves what an author built
  with citizens and names which citizens a deployment should have. It carries exactly one site: settings,
  pages (body, trust, theme pin, plugin selection, instance-settings slots, SEO, role access, slug
  aliases), per-component metadata, media, and a **Packages** list.
- `Packages[]` is purely referential — `Kind.key@version`, version mandatory, never bytes. Entries resolve
  from `library/`, then `IdeaCms:PackagesDirs`/`--packages-dir` (matched by manifest, not filename), then a
  NuGet feed (`CompositeIdeaListPackageResolver`; downloads cached in `library/.nuget-cache/`).
- **Apply order:** install Packages in listed order (each idempotent), then validate every page's
  `Uses[]` against the catalog, then write media/site/settings/pages/metadata. An unmet `Uses[]` throws
  with nothing written.
- **Reconciliation:** pages match by `Uid`, then `(SiteId, Slug)`, both scoped to the target site, so an
  import adopts an independently seeded page and a page whose uid exists on another site gets a fresh uid
  (a copy, never a move). Media is adopted by SHA-256 and every media uid reference is remapped through
  an old→new map. Re-import is a no-op. The target site is matched by key and created when absent
  (`--into-site` overrides).
- **Trust:** the bundle's `Author` trust is honoured but announced; `--untrusted` downgrades it.
  `--prune` (soft-delete pages absent from the list) is opt-in.
- CLI: `--export-idealist`, `--import-idealist`, `--compose-idealist`.
- **Boot.** `IdeaCms:Idealist` (env `IDEACMS_IDEALIST`) absent = *vanilla*: install everything in `library/`,
  best-effort. Present = *custom*: apply that idealist and abort startup on any failure
  (`BootProvisioning.ApplyAsync`).

### 4.10 Sites and domains {#M.IDEA-§4.10}
- One deployment can serve many domains. `ISiteResolver` scores the request host against each site's
  `HostBindings` (comma/semicolon/whitespace separated, case-insensitive, URL-tolerant, port-agnostic
  unless a port is named). Precedence: `host:port` → `host` → `*.domain` (never the apex) → `*` → the
  default site. Ties break by the default flag, then lowest id.
- A site with no bindings answers every host it is the default for, so a single-site install needs no
  configuration.
- `PageHost` reads the host from `NavigationManager.BaseUri`, not `HttpContext` (null after the circuit
  connects). `CmsSiteContext.Host` carries the request host.
- The bare route `/` forwards to the slug in the Site-scope `page.frontpage` setting, else the Host-scope
  one (default `frontpage`).
- `IPageTree.ChildrenOfSlugAsync(siteId, slug)` scopes a slug lookup to a site; `Guid.Empty` falls back
  to the unscoped, ordered lookup.
- **Admin → Sites** creates sites, edits bindings, moves the default, sets the default theme and theme
  mode, and probes "which site answers this host?" with the render path's rule. A binding another site
  claims is refused; a site with pages, or the default site, cannot be deleted.
- `X-Forwarded-Host` is **not** trusted: forwarded headers are accepted from any peer
  (`KnownProxies` cleared) and only `X-Forwarded-For`/`-Proto` are forwarded, because a client-supplied
  host must never choose the tenant. Enable it only after populating `KnownProxies`.

### 4.11 Media {#M.IDEA-§4.11}
- A page references an asset only by `/_media/{uid}`. `Media:Provider` chooses the store — `local`
  (default) or `azure` — without changing any markup or row. Azure needs
  `Media:Azure:ConnectionString` or `BlobServiceUri` (via Vault); without them startup fails closed.
- When a store can mint a URL, `/_media/{uid}` 302s to a short-lived SAS (or, with `PublicRead` +
  `PublicBaseUri`, a plain cacheable URL) and blob storage serves Range requests; otherwise the app
  streams with `Accept-Ranges`/206, an ETag from the stored SHA-256 (304 on match), `Last-Modified`,
  and `Cache-Control`. `image/`, `text/`, `video/`, `audio/` and PDF are inline dispositions.
- Uploads hash in flight over one sequential pass and spill past `InlineThresholdBytes` (2 MB); memory
  is bounded by the threshold, not the file.
- `--extract-media` lifts inline base64 out of bodies (→ `<Component.MediaImage uid="…" />`) and
  stylesheets (→ `/_media/{uid}`), deduplicating by SHA-256 and leaving undecodable data inline as a
  reported failure. `--upload-media <file…>` streams local files into the configured store.
- Deleting media is soft; blobs are left behind. Nothing migrates existing inline rows into blob storage.

### 4.12 Page lifecycle: history, workflow, redirects {#M.IDEA-§4.12}
- **History.** `Pages` is a SQL Server system-versioned temporal table; Admin's Page History panel reads
  `FOR SYSTEM_TIME` and `RestoreAsync` copies a snapshot's content onto the current row, re-stamping
  trust from the restoring user.
- **Workflow.** `WorkflowDefinition` (name, initial state, `IsDefault`) + role-gated
  `WorkflowTransitionDef`s. `WorkflowService.TransitionPageAsync` validates the transition, checks the
  role (Admins bypass), and sets `IsPublished` only for the state named `Published`. Creating a default
  demotes the previous one. A manifest's `defaultWorkflow` is advisory and not enforced.
- **Slug history.** Changing a slug writes a `PageSlugHistory` row; vanity slugs are added idempotently.
  `PageHost` checks `SlugRedirectService` before returning 404 and writes a real **301** with `Location`
  when the response has not started (falling back to `NavigateTo`). No self-redirects; unpublished or
  disabled targets do not redirect.
- **Tagless instance settings** are versioned in `WidgetPlacementSettings` + history; rollback restores a
  snapshot while advancing the version.

### 4.13 Themes and light/dark mode {#M.IDEA-§4.13}
- Every Theme ships a light and a dark palette, each WCAG 2.2 AA. The active mode is
  `data-theme-mode="light"|"dark"` on `<html>`; each `theme.css` overrides its palette for the other mode.
- `Site.DefaultThemeMode` is the first-visit default. `App.razor` resolves the site and sets the attribute
  server-side; a synchronous inline script, first in `<head>`, then applies the visitor's saved choice
  (`localStorage` `mindattic.theme-mode`), so neither first nor return visits flash the wrong mode.
- `Plugin.ThemeToggle` (BeforeBody) flips the mode and stores the choice.
- First-party themes: Autumn, Cyberspace, Hardware, Ideas, Spring, Summer, Winter. A page whose theme
  key does not resolve falls back to the built-in `bootstrap` theme.

### 4.14 Deployment and the public demo {#M.IDEA-§4.14}
- One build runs as **two deployments** on one Linux App Service plan: **`mindattic`** (the company
  site, https://mindattic.azurewebsites.net) and **`mindattic-ideas-demo`** (a public, vanilla install,
  https://mindattic-ideas-demo.azurewebsites.net). Runbook: [`docs/DEPLOYMENT.md`](DEPLOYMENT.md).
- `infra/main.bicep` (+ `infra/webapp.bicep` per site) provisions the plan, both web apps, Azure SQL
  (Entra-only), a storage account (no shared keys, no public blobs), Key Vault, and role assignments.
  `appName = mindattic-ideas` is the name base of every shared resource. Bicep owns every app setting;
  Key Vault references live in the template and `infra/provision.ps1` generates the secret values.
- **Passwordless.** Each site has a system-assigned managed identity for SQL, Blob Storage and Key Vault.
  The app identity holds `db_datareader` + `db_datawriter` only, so schema changes come only from CI.
- **App-setting names are alphanumeric**, because App Service on Linux rewrites `.` and `-` in setting
  names; Authentication and Vault match keys by letters and digits.
- **CI** (`.github/workflows/azure-deploy.yml`, push to `main`, GitHub OIDC): build (restore from the
  vendored `lib/local-packages/` + nuget.org, full NUnit suite, publish, generate `seed/demo.idealist`
  and the idempotent migration script) → migrate (company database and the demo template, Entra token,
  single-run firewall rule) → deploy the company site and wait until `/_health` reports this commit ready
  and `/` answers → deploy the demo → reset the demo. The two sites never cold-start together on the
  single-core B1 plan. Deploy never proceeds after a migrate that ran and failed.
- **Startup:** the server listens first, then runs the boot sequence (discovery, seed, admin bootstrap,
  provisioning) through `StartupGate`: transient failures (SQL, socket, timeout, Azure identity) are
  retried with backoff, a permanent one exits with code 1, never an unhandled abort (exit 134). Until the
  sequence finishes every request but `/_health` gets `503 starting`.
- **`/_health`** is a liveness probe that never touches the database and answers as soon as the server
  listens; `X-IdeaCms-Ready` and `X-IdeaCms-Version` (build + commit) report boot state.
- **The demo** is reset hourly by `.github/workflows/demo-reset.yml`: rotate the admin password, stop the
  demo and pin the new password, replace the demo database with a copy of `IdeaCmsDemoTemplate`,
  empty its media, start it (its one boot on the empty database seeds `admin` from the new password),
  sign in for real, then publish the login. The demo identity reaches only its own vault and containers; the company
  identity can read one demo secret (`credentials`). Demo admins cannot run code: unsigned packages are
  refused and markup is sanitized.
- Product hooks used by the demo, all inert unless configured:
  `MindAttic:Auth:Bootstrap:RequirePasswordChange=false`; `IdeaCms:Packages:StorageRoot`; `IDemoAccess`
  (demo URL and status, never credentials); `POST /_demo/reveal` returns the login only for a
  server-verified Turnstile token bound to the `demo-reveal` action, 5/min per IP, `no-store`, 503 when
  Turnstile keys are absent.

## 5. The Laws {#M.IDEA-§5}

> **Inherited.** IdeaCms inherits every org-wide law in
> [`MindAttic.HouseRules.md`](../../MindAttic.HouseRules.md) by reference. Do not restate them here. Of
> particular weight: whole-number versioning ([HOUSE-LAW-1](../../MindAttic.HouseRules.md#HOUSE-LAW-1)),
> soft-disable ([HOUSE-LAW-2](../../MindAttic.HouseRules.md#HOUSE-LAW-2)), Vault credentials
> ([HOUSE-LAW-3](../../MindAttic.HouseRules.md#HOUSE-LAW-3)), Legion LLMs
> ([HOUSE-LAW-4](../../MindAttic.HouseRules.md#HOUSE-LAW-4)), guarded packaging
> ([HOUSE-LAW-5](../../MindAttic.HouseRules.md#HOUSE-LAW-5)), one-engine-many-front-doors
> ([HOUSE-LAW-6](../../MindAttic.HouseRules.md#HOUSE-LAW-6)), MindAttic.Authentication
> ([HOUSE-LAW-7](../../MindAttic.HouseRules.md#HOUSE-LAW-7)), and verified-DoD
> ([HOUSE-LAW-8](../../MindAttic.HouseRules.md#HOUSE-LAW-8)).

These are the **project-specific** laws:

- **{#M.IDEA-LAW-1} Naming & identity lock.** A citizen's forever-identity is `(ContentKind Kind, string Key,
  int Version)` — never the CLR type name. The four kinds are Page · Plugin · Theme · Component under
  `IdeaBase`; "Idea" is never a kind. MindAttic's `ComponentBase` owns the bare name; Blazor's is aliased
  `BlazorComponentBase`. On any future framework name clash, alias the framework side.
- **{#M.IDEA-LAW-2} Frozen SDK, MAJOR=1 forever.** `Abstractions` references only
  `Microsoft.AspNetCore.Components` + `System.Text.Json`; its surface is append-only (enums grow by
  appending ordinals, attributes by optional init-only properties, interfaces by default methods). Ordinal
  3 of `ContentKind` is reserved and never reused.
- **{#M.IDEA-LAW-3} One render primitive, no zones, no routing.** All rendering is `DynamicComponent` /
  `FreeFormPage` through the single `PageHost` catch-all resolving `(SiteId, Slug)`. No
  zones/panes/slots/grids; no per-page routes. Reserved routes live under `/_` (`/_ideas`, `/_media`,
  `/_health`, `/_ma-auth`, `/_demo`) so they can never shadow a page slug.
- **{#M.IDEA-LAW-4} Fixed CSS cascade, enforced in one place (`CmsHead`).** GLOBAL → THEME → PAGE →
  COMPONENT → DOM INLINE, guaranteed by cascade layers ([§4.7](#M.IDEA-§4.7)); never reordered elsewhere.
  The shared asset route is locked at `/_ideas/{Kind}/{key}/{version}/{**path}`.
- **{#M.IDEA-LAW-5} Trust at write time, sanitized at render.** On save, `BodyTrust = Author` iff the writer
  holds `Cms.AuthorRawMarkup` (Admin), else `Untrusted`. The single `IRawContentGate` is the only place a
  `MarkupString` is born, and it sanitizes **every** body; trust decides only what a body may additionally
  keep ([§4.6](#M.IDEA-§4.6)). Demotion is a deliberate `AuthorTrustVersion` epoch bump, never a silent
  re-render; already-published Author pages keep rendering.
- **{#M.IDEA-LAW-6} ALC defer-to-default.** A per-`.idea` collectible `AssemblyLoadContext` defers
  `SharedContracts.DeferToDefaultPrefixes` (Abstractions, Core, Microsoft.*, System.*, …) to the default
  context so a package's base types unify by reference identity with the host's. Host assemblies are
  forbidden in a package `bin/`.
- **{#M.IDEA-LAW-7} A page is never invalid.** A missing or disabled reference degrades to a placeholder and
  raises a Missing or Disabled Admin Inbox alert. The render thread never throws.
- **{#M.IDEA-LAW-8} Two version axes, never conflated.** `manifestVersion` (file-format host gate) is
  distinct from `sdk` (runtime contract floor); both are integers.
- **{#M.IDEA-LAW-9} Use the shared packages, don't reinvent.** Credentials come from MindAttic.Vault, sign-in
  from MindAttic.Authentication, LLM access from MindAttic.Legion, media storage from MindAttic.Media.
  Ideas adds capability to those packages rather than growing its own copy.
- **{#M.IDEA-LAW-10} Only signed packages install.** Every install path goes through
  `PackageInstallService.InstallAsync`, which verifies the content signature against host-configured
  trust before reading the manifest ([§4.8](#M.IDEA-§4.8)).
- **{#M.IDEA-LAW-11} A demo is a deployment, not a feature.** The product contains no routine that resets or
  deletes a site's content on a schedule; a showroom is a separate vanilla install reset by its own
  operator ([§4.14](#M.IDEA-§4.14)).

## 6. Verified state {#M.IDEA-§6}

**Build/test evidence (2026-10-03):** `dotnet test src/IdeaCms.Tests` → **Passed: 586, Failed: 0,
Skipped: 0**. The SQL Server temporal proof (`PageHistorySqlServerTests`) is `[Explicit]` and runs
against LocalDB on demand.

**Live:** the company site and the demo run on Azure from the CI pipeline. The hourly `demo-reset.yml`
workflow passes (run 37152987331, 2026-10-03: stop, pin, swap, start, real sign-in, publish). The
`azure-deploy.yml` run 37156225976 (2026-10-03) is green end to end: the company site listened 25 s after
`dotnet` started, finished its boot sequence 33 s later, and reported the pushed commit ready; then the
demo deployed and its reset signed in with the new password.

Proven working (each cited in [`USER_STORIES.md`](USER_STORIES.md)):
- ✅ Abstractions SDK (kind bases, `[Idea]`/`[Uses]`/`[Setting]`, plugin slots, page tree, metadata store).
- ✅ Core EF model, temporal `Pages`, Authentication package schema, seed.
- ✅ Convention discovery, persisted catalog, ALC load + unification.
- ✅ Tag grammar, include expander, typed-attribute coercion, placeholder + Admin Inbox degradation.
- ✅ Body sanitization at every trust level; page, pack and CI validation.
- ✅ Cascade layers, Component-beats-Page, CSS conflict merger.
- ✅ `.idea` packaging, content signing, install with dependency gates and hash-conflict detection.
- ✅ Instance settings, Copy/Paste Configuration.
- ✅ `.idealist` export/import/compose and boot provisioning.
- ✅ Multi-domain sites, slug history (301), workflow, page history.
- ✅ Media on local disk and Azure Blob, `--extract-media`, `--upload-media`.
- ✅ Deployment packaging contract (vendored feed, health route, security floors), demo reveal endpoint.

## 7. Active frontier {#M.IDEA-§7}

No RFC is open. The CMS is in **enhance-only** mode per [§2](#M.IDEA-§2): new capability arrives as new
stories, new whole-number content versions, and new library `.idea`s. Open stories (⬜/🟡) are listed in
the [`USER_STORIES.md`](USER_STORIES.md) priority backlog; the library's frontier is in
`library/docs/BIBLE.md`.

## 8. Quality bar {#M.IDEA-§8}

Definition of done (a feature is `✅` only when *verified*, never merely asserted — see
[HOUSE-LAW-8](../../MindAttic.HouseRules.md#HOUSE-LAW-8)):

1. Clean `dotnet build` (0 warnings) on `IdeaCms.slnx`.
2. Green NUnit tests; a `✅` story names the test that proves it.
3. For anything user-facing, an e2e or lifecycle assertion, or an observed live run.
4. No new fact duplicated: a fact lives in exactly one layer and is cited by `{#id}` elsewhere. Code
   comments cite bible sections and laws (`M.IDEA-§4.5`, `M.IDEA-LAW-4`), never change records.
5. Whole-number versioning; soft-disable over hard-delete; secrets via Vault.

## 9. Glossary {#M.IDEA-§9}

- **.idea** — a plain zip whose required members are `idea.json` and `idea.sig.json`; the install unit.
- **Idea / IdeaBase** — the shared base of all content kinds and the package format; never a kind.
- **Page** — content, a CMS DB row (Data or Code), resolved by `(SiteId, Slug)`.
- **Plugin** — a site-wide `.idea` kind (ordinal 1): activates a behavior across the whole rendered page
  without occupying a token position (e.g. Tooltip, OutfitFont, Header, ThemeToggle). Selected per page
  in Admin (or inherited from `plugins.default`); may be placed inline with `<Plugin.X />`; renders
  `BeforeBody` or `AfterBody`.
- **Component** — an inline-placed `.idea` kind (ordinal 4): renders at its `<Component.X />` tag; can
  nest other Components via `ChildContent`. `ComponentBase` is MindAttic's; Blazor's is
  `BlazorComponentBase`.
- **Theme** — layout chrome + one `@Body` hole + a CSS bundle with light and dark palettes.
- **Citizen** — any installed Page type, Plugin, Component or Theme.
- **ContentKind** — `Page=0 · Plugin=1 · Theme=2 · Component=4`; ordinal 3 reserved.
- **Data page / Code page** — free-form DB body (zero deploy) vs a compiled `PageBase` subclass.
- **Instance settings** — a citizen instance's typed `[Parameter]` values ([§4.5](#M.IDEA-§4.5)).
- **`.idealist`** — the portable site + package-list artifact ([§4.9](#M.IDEA-§4.9)).
- **Content signature** — the RSA-PSS signature in `idea.sig.json` ([§4.8](#M.IDEA-§4.8)).
- **Catalog (`CmsContentDefinition`)** — the one persisted registry of all citizens.
- **ALC** — the per-package collectible `AssemblyLoadContext` used to load `.idea` citizens.
- **Raw-content gate (`IRawContentGate`)** — the sole `MarkupString` chokepoint; sanitizes every body.
- **Admin Inbox** — DB-backed dedup alert surface for render-time degradation and install conflicts.
- **Trust (`ContentTrust`)** — `Author` vs `Untrusted`, set at write time ([M.IDEA-LAW-5](#M.IDEA-LAW-5)).
- **Site** — a tenant resolved from the request host ([§4.10](#M.IDEA-§4.10)).
- **Library** — the `library/` directory: the single home of all first-party Themes, Plugins and
  Components (canon code MAIL).
- **UiUx** — `MindAttic.Components` (its `web/` folder is the shared asset package, formerly
  the MindAttic.UiUx repo), the build-free upstream raw source for some library citizens' engines,
  consumed by pinned-tag jsDelivr URL.
