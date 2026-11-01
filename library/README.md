# IdeaCms.Library

The **first-party library of `.idea` citizens** for [IdeaCms](../README.md) — the `library/` half of the
IdeaCms repo, home of every Theme, Plugin, and Component that ships with the CMS.

The CMS never references this library at compile time. It only installs the packed `.idea` files as
optional content. Each project here compiles against the frozen `IdeaCms.Abstractions` SDK only.

## The one rule: the asset bundle is the single source of truth

Each citizen **owns its assets** in its own `assets/` folder. That bundle serves all three consumers:

| Consumer | Uses the bundle as… |
|---|---|
| **Raw `.html` pages** | links `assets/*.css` / `assets/*.js` directly (see `Themes/Cyberspace/demo.html`) |
| **Standalone Blazor apps** | references the component RCL, or links the same `assets/` |
| **The IdeaCms CMS** | uploads the packed `.idea` (assets bundled into `wwwroot/`) |

Three packagings of one thing — not three projects.

## Layout

```
library/
  Themes/      Autumn, Cyberspace, Hardware, Ideas, Spring, Summer, Winter  (7)
  Plugins/     AtticFont, BackHomeM, BackToTop, Breadcrumbs, Cyberspace, Footer, Header,
               NavMenu, OutfitFont, PinFooter, PoweredBy, SacredGeometry, SocialLinks,
               ThemeToggle, Tooltip  (15)
  Components/  Accordion, AppLaunch, Callout, Card, Carousel, ChiMesh, Claudia,
               CodeBlock, ContactForm, FromHtml, FromMd, Frontpage, Gallery,
               HardwareHero, HelloWorld, Hero, IdeaCmsBrochure, IdeaCmsFrontpage,
               LegionPersonas, MediaImage, MediaLink, MindAtticFrontpage, ModalPopup,
               ProjectBrochure, ProjectGrid, TabBoard, TableOfContents, Tabs, Textbox,
               VideoEmbed, WebSnapshot  (31)
  _Shared/     linked-source helpers (ShadowDomAttach.cs)
  tools/       pack-all.ps1, publish-nuget.ps1, codex.ps1
  dist/        packed, signed *.idea — copied into the CMS host's library/
```

**53 `.idea`s total** ([MAIL-§4.1](docs/BIBLE.md#MAIL-§4.1)). Each project is its own small csproj so each `.idea` is independently
versioned and uploadable. Common build settings + the Abstractions reference live once in
`Directory.Build.props`.

## Build & pack

Build one citizen:

```pwsh
dotnet build -c Release Plugins/Tooltip
```

Build everything:

```pwsh
dotnet build -c Release IdeaCms.Library.slnx
```

Build, pack and sign everything (`-Install` also copies the packages into the CMS host's `library/`):

```pwsh
powershell -ExecutionPolicy Bypass -File tools\pack-all.ps1 -Sign -Install
```

Pack one citizen to `dist/` (the `ma-idea` CLI is `../src/IdeaCms.Sdk`):

```pwsh
dotnet run --project ../src/IdeaCms.Sdk -- pack `
  --assembly Plugins/Tooltip/bin/Release/net10.0/IdeaCms.Plugin.Tooltip.dll `
  --out ./dist `
  --wwwroot Plugins/Tooltip/assets `
  --refs ../src/IdeaCms.Abstractions/bin/Debug/net10.0
```

Then inspect or verify:

```pwsh
dotnet run --project ../src/IdeaCms.Sdk -- inspect ./dist/IdeaCms.Plugin.Tooltip.V1.idea
dotnet run --project ../src/IdeaCms.Sdk -- verify ./dist
```

See [`docs/AUTHORING.md`](../docs/AUTHORING.md) for the full authoring guide (adding a new citizen, composing,
uploading).

## Codex docs

- [`docs/BIBLE.md`](docs/BIBLE.md) — L0 source of truth (architecture, laws)
- [`docs/AMENDMENTS.md`](docs/AMENDMENTS.md) — L1 pending decisions not yet folded into the bible (normally empty)
- [`docs/USER_STORIES.md`](docs/USER_STORIES.md) — L2 test-cited stories
