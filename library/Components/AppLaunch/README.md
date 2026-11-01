# AppLaunch

A landing-page tile that opens a hosted app **borderless** — the launcher for the kind of project
(ExperimentRTS, Hyperspace) whose landing page exists to get you into the app, not to be read.

## The browser rule this is built around

There is no single call that produces "a separate borderless fullscreen window", and it is worth
knowing why before choosing a mode. **A page cannot put a window it opened into fullscreen.** Calling
`requestFullscreen()` on a popup's document from the opener is rejected —

```
REJECTED: TypeError Permissions check failed
```

— even when opener and popup are same-origin. The activating gesture has to happen *inside* the
target window. So this component is a ladder, not a switch:

| Mode | What you get | Cost |
|---|---|---|
| `fullscreen` *(default)* | Overlay iframe in **this** window + the Fullscreen API. Zero browser chrome. | One click. No permission prompt. **The reliable one.** |
| `window` | A real separate window; it goes borderless on **its own** first gesture. | The opened page must carry this component's script — i.e. be an Ideas page with an AppLaunch on it. |
| `inline` | Embedded iframe, no launch step, with a fullscreen affordance. | None. |

Below all of them: if `requestFullscreen` is missing or refused, the overlay stays a `position:fixed`
cover — borderless within the tab rather than borderless on the screen. If a popup is blocked,
`window` mode falls back to the overlay rung rather than doing nothing.

## Usage

```html
<!-- the common case: launch borderless in this window -->
<Component.AppLaunch url="/_ideas/Component/experimentrts/1/index.html"
                     title="ExperimentRTS"
                     blurb="Desert Planet — a Babylon.js RTS sandbox."
                     buttontext="Play" />
```

### A genuinely separate borderless window

Compose it from two pages. The launcher points at an Ideas **page**, not at the raw asset, because
the opened document is the one that has to arm itself:

```html
<!-- /projects/hyperspace — the landing page -->
<Component.AppLaunch url="/hyperspace" title="Hyperspace" mode="window" width="1280" height="800" />

<!-- /hyperspace — the app host; carries applaunch.js, so ?ma-fs=1 arms it -->
<Component.AppLaunch url="/_ideas/Component/hyperspace/1/index.htm" title="Hyperspace" mode="inline" />
```

The launcher opens `/hyperspace?ma-fs=1`. On that page the script raises a curtain reading *"Click
anywhere to go fullscreen"*; the first click goes fullscreen and the curtain is removed.

**Why a curtain and not a click listener.** An app-host page is mostly a full-bleed `<iframe>`, so
the visitor's first click lands *inside* it. A click in a nested browsing context never reaches the
host document, so a bare `document.addEventListener('click', …)` would never fire and the window
would stay chrome'd forever. The curtain intercepts exactly one click — the same "click to play"
splash every browser game uses, for the same reason — then gets out of the way. It also self-removes
after 15 seconds, so it can never trap the page.

## Parameters

| Name | Type | Default | Description |
|---|---|---|---|
| `url` | `string` | — | The app to launch: an `/_ideas` asset path, an Ideas page slug, or an absolute URL. Passed through `SafeUrl`, so `javascript:`/`data:` are neutralised. |
| `title` | `string` | `"Application"` | Tile heading, the iframe's accessible name, and the dialog label. |
| `blurb` | `string` | — | One line under the title. Omit to suppress. |
| `buttontext` | `string` | `"Launch"` | Launch button label. |
| `mode` | `string` | `"fullscreen"` | `fullscreen` · `window` · `inline`. An unknown value falls back to `fullscreen`. |
| `posteruid` | `string` | — | Managed-media uid for the tile poster, served from `/_media/{uid}`. |
| `posterurl` | `string` | — | Poster image URL. Ignored when `posteruid` is set. |
| `width` | `int` | `1280` | Separate-window width (`mode="window"`). |
| `height` | `int` | `800` | Separate-window height (`mode="window"`). |
| `target` | `string` | — | Named window target, so re-launching reuses one window instead of stacking them. |
| `ratio` | `string` | `16/9` | Aspect ratio for the inline frame and the poster. |
| `showexpand` | `bool` | `true` | Whether inline mode shows its fullscreen button. |
| `expandtext` | `string` | `⤢` | Inline-mode fullscreen button label. |
| `config` | `string` | — | JSON blob carrying any of the above. Explicit parameters win. |
| `cssclass` / `cssstyle` / `padding` / `margin` / `align` | `string` | — | The standard library layout parameters. |

## Hosting the app itself

Ideas can serve the app as well as launch it. Pack the built bundle as an asset-only `.idea` — a
`ComponentBase` subclass that declares **no** `StylesheetUrls`/`ScriptUrls` (the game's bundle must
load inside its own iframe, never be hoisted into the landing page's `<head>`), with the build output
as the package `wwwroot`:

```pwsh
# a Vite app needs a relative base, or its absolute /assets/… paths escape the mount
npx vite build --base=./ --outDir dist
dotnet run --project src/IdeaCms.Sdk -- pack `
  --assembly bin/Release/net10.0/IdeaCms.Component.ExperimentRts.dll `
  --out library/dist --refs src/IdeaCms.Abstractions/bin/Debug/net10.0 `
  --wwwroot dist
```

It then serves at `/_ideas/Component/{key}/{version}/index.html` with correct MIME types.

**Known limits of that route:** there is no default document (a directory URL returns 400, so link
`index.html` explicitly) and no SPA fallback (client-side deep links 404). Neither matters for a
canvas app; both matter for a router-based SPA.

## CSS

Two surfaces. The tile (`.ma-applaunch*`) inherits colour and is themeable through
`--ma-applaunch-bg`, `--ma-applaunch-line`, `--ma-applaunch-radius`, `--ma-applaunch-ratio`,
`--ma-applaunch-width`. The overlay is deliberately **not** themeable: it is a viewport takeover, so
its geometry carries `!important` to survive any theme that resets `position`/`inset`/`z-index` on
descendants.

## Accessibility

The overlay is `role="dialog" aria-modal="true"`, takes focus on open, returns focus to the launch
button on close, and closes on Escape. Native fullscreen swallows Escape to exit fullscreen first,
which fires `fullscreenchange` — both paths tear the overlay down, so one press always leaves.
`prefers-reduced-motion` disables the tile transition and the curtain animation.

## Settings

All parameters are instance settings (M.IDEA-§4.5); `url`, `title`, `blurb`, `buttontext`, poster, `expandtext` and `config` are content (not copied by Copy/Paste Configuration). Added:

- **Layout:** `maxwidth` (22rem), `radius` (14px), `titlesize` (1.15rem)
- **Colors:** `background`, `bordercolor`, `buttonbackground`, `buttonbordercolor`, `buttonhoverbackground` — written to the `--ma-applaunch-*` properties above
- **Behavior:** `showposter` (true), `animations` (true; off adds `ma-applaunch--no-anim`)
