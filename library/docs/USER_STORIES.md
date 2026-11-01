---
codex: 1
project: IdeaCms.Library
code: MAIL
layer: stories
status: living
updated: 2026-10-03
---

# IdeaCms.Library — User Stories

> ✅ done (shipped & verified) · 🟡 partial · ⬜ planned. Every ✅ cites the proof. `library/` has no
> test project of its own; proofs are a clean build, a CMS-suite test in `../src/IdeaCms.Tests`,
> or an observed demo (see [MAIL-§8](BIBLE.md#MAIL-§8) and [HOUSE-LAW-8](../../../MindAttic.HouseRules.md)).

## Epic A — Authoring a citizen

- **MAIL-US-A1 ✅** As a citizen author, I can add a Theme/Plugin/Component as a tiny RCL and have its
  identity come from convention (namespace tail = key, `V{n}` = version), so no per-project key/version
  config exists. *(verified by `dotnet build -c Release IdeaCms.Library.slnx`; rule at
  [MAIL-LAW-2](BIBLE.md#MAIL-LAW-2).)*
- **MAIL-US-A2 ✅** As a citizen author, I get common build settings and the one Abstractions reference
  for free, so my `.csproj` stays tiny. *(verified by [`Directory.Build.props`](../Directory.Build.props)
  + the clean solution build.)*
- **MAIL-US-A3 ✅** As a citizen author, my css/js live in a plain `assets/` folder (not `wwwroot/`) so the
  Razor SDK never causes cross-host static-asset collisions. *(verified by the clean solution build; rule
  at [MAIL-LAW-5](BIBLE.md#MAIL-LAW-5).)*
- **MAIL-US-A4 🟡** As a site builder, I can compose ordinary-website UI from the **baseline set**
  ([MAIL-§4.1](BIBLE.md#MAIL-§4.1)). *(built, packed and validated —
  `ShippedContentValidationTests.EveryShippedPackage_PassesCitizenValidation`; 🟡 until each baseline
  citizen's interactive behavior is observed.)*
- **MAIL-US-A5 ✅** As a page author, every citizen exposes its look and behavior as instance settings
  whose defaults reproduce the design, so I can tune one instance without forking it. *(verified by
  `ShippedContentValidationTests.EveryShippedPackage_PassesCitizenValidation` (settings declarations) and
  the CMS `InstanceSettingsTests`; rule at [MAIL-LAW-9](BIBLE.md#MAIL-LAW-9).)*

## Epic B — The one bundle, many consumers

- **MAIL-US-B1 ✅** As a raw-HTML author, I can link a citizen's `assets/*` directly and see it render with
  no CMS, build, or Blazor. *(verified by [`Themes/Cyberspace/demo.html`](../Themes/Cyberspace/demo.html)
  linking the same `theme.css` the `.idea` bundles.)*
- **MAIL-US-B2 ⬜** As a standalone Blazor app, I can reference a citizen RCL (or link the same `assets/`)
  and get identical output to the CMS. *(no standalone-app harness yet — [RFC 0001](rfc/0001-component-test-harness.md).)*
- **MAIL-US-B3 ✅** As the CMS, I can install a packed `.idea` and serve its bundle under the citizen
  mount. *(verified by the CMS `RenderPipelineTests` and live renders serving `/_ideas/...` mounts with
  HTTP 200.)*

## Epic C — Composition by string id

- **MAIL-US-C1 ✅** As a Theme author, I can compose installed Plugins by string key without a project
  reference. *(verified by the clean build of `Themes/Cyberspace`, whose `[Uses]` edges are recorded on
  [theme.cyberspace](data/components.json).)*
- **MAIL-US-C2 ✅** As a Component author, I can compose other citizens by id (Frontpage → tooltip,
  LegionPersonas → sacredgeometry). *(verified by the clean solution build; edges on
  [component.frontpage](data/components.json) and [component.legionpersonas](data/components.json).)*

## Epic D — Catalog & lifecycle

- **MAIL-US-D1 ✅** As a maintainer, I can read one catalog of every shipped citizen. *(verified by
  `tools/codex.ps1 doctor` schema + id-uniqueness checks over [`components.json`](data/components.json):
  53 rows — 7 Themes, 15 Plugins, 31 Components.)*
- **MAIL-US-D2 ✅** As a maintainer, every citizen versions by whole numbers only. *(verified by the `V{n}`
  classes and [HOUSE-LAW-1](../../../MindAttic.HouseRules.md).)*
- **MAIL-US-D3 ✅** As a maintainer, I can rebuild, repack and sign the whole library in one command.
  *(verified by `tools/pack-all.ps1 -Sign -Install` producing the 53 signed packages the CMS suite
  validates in `ShippedContentValidationTests`.)*

## Epic F — Apps and project pages

- **MAIL-US-F1 ✅** As a project owner, my landing page can open the app **borderless**, because
  `Component.AppLaunch` overlays a full-viewport iframe and calls the Fullscreen API on the launch click.
  A page cannot fullscreen a window it opened, so the component is a fallback ladder: `fullscreen`
  (default) → `window` (a separate window that arms itself via `?ma-fs=1` on its own first click) →
  `inline`. *(verified by demo with Playwright against a live host: `document.fullscreenElement =
  .ma-applaunch-overlay` at the full viewport, Escape restored; `mode="window"` went fullscreen on its
  own first click. Zero page errors.)*
- **MAIL-US-F2 ✅** As a project owner, Ideas can **host** the app as well as launch it, because a built
  bundle packs as an asset-only Component and serves from `/_ideas/Component/{key}/{version}/…`.
  *(verified by demo: ExperimentRTS packed, installed and served — `index.html` and the entry chunk 200
  with correct MIME types, Babylon booted inside the fullscreen overlay.)*
- **MAIL-US-F3 ✅** As a reader, every project gets a brochure page that opens the same way, because
  `Component.ProjectBrochure` renders the shared identity (status, tagline, tech badges, links, lead
  image) and hands `ChildContent` back for whatever that project needs. *(verified by demo against a live
  host: `/projects/mindattic-vault`, `/projects/hyperspace`, `/projects/experimentrts` and
  `/projects/prose` render with zero `ma-missing` placeholders.)*

## Priority backlog

1. **MAIL-US-B2** — a standalone-Blazor-app smoke harness ([RFC 0001](rfc/0001-component-test-harness.md)).
2. **MAIL-US-A4** — observe the baseline set's interactive behavior.
