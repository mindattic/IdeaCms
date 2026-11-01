---
codex: 1
project: IdeaCms.Library
code: MAIL
layer: rfc
status: planned
updated: 2026-10-03
---

# RFC 0001 — A component smoke-test + pack round-trip harness

## Problem
`library/` has **no test project**. The CMS suite validates every shipped package
(`ShippedContentValidationTests`) and the install/render pipeline (`RenderPipelineTests`), but a
citizen's own rendering and its standalone-Blazor consumer are proven only by a clean `dotnet build` plus
hand-observation (HelloWorld's click counter, `Themes/Cyberspace/demo.html`). That leaves
[MAIL-US-B2](../USER_STORIES.md) at ⬜ and [MAIL-US-A4](../USER_STORIES.md) at 🟡 against
[HOUSE-LAW-8](../../../../MindAttic.HouseRules.md). We want each citizen's promises — *builds*, *packs to a
valid `.idea`*, *renders from its bundle* — machine-verified inside `library/`.

## Options compared
1. **xUnit test project that builds + invokes the SDK `pack` and asserts on `dist/*.idea`** (zip contents:
   exactly one DLL in `bin/`, `assets/` present as `wwwroot/`, SHA-256 manifest). Closest to
   [HOUSE-LAW-5](../../../../MindAttic.HouseRules.md); pure CLI, no browser.
2. **bUnit render tests** for the Razor components (HelloWorld counter, Textbox parameters/attributes).
   Proves render/interactivity but pulls a test web stack and does not exercise packaging.
3. **Playwright over each `demo.html` + a thin standalone Blazor host.** Highest fidelity (the raw-HTML
   and standalone-app consumers), highest cost/flakiness.

## Decision
Stage it: **(1) first** as the cheapest high-value coverage (build + pack + artifact validation for all 53
catalog rows, table-driven from [`components.json`](../data/components.json)), then **(2)** for the two
interactive/parameterized components. Defer (3) until a host harness exists.

## What NOT to do
- Do **not** add a `ProjectReference` from any component to the test project, or otherwise break
  [MAIL-LAW-3](../BIBLE.md#MAIL-LAW-3) / [MAIL-LAW-4](../BIBLE.md#MAIL-LAW-4).
- Do **not** move assets into `wwwroot/` to make testing easier — [MAIL-LAW-5](../BIBLE.md#MAIL-LAW-5) stands.
- Do **not** mark stories ✅ until the harness is green in CI.

## Phased plan (with risk)
- **P1** — test project + table-driven build/pack/validate over `components.json`. *Risk:* signing needs the
  Vault `PackageSigning` bucket; skip signature checks with a clear message when it is absent.
- **P2** — bUnit render assertions for `component.helloworld` and `component.textbox`. *Risk:* test web stack
  drift vs net10.0.
- **P3** — host/browser harness for the raw-HTML and standalone-app consumers. *Risk:* flakiness; keep out
  of the required gate initially.

## Graduates into
[MAIL-§6](../BIBLE.md#MAIL-§6) (verified state), [MAIL-§8](../BIBLE.md#MAIL-§8) (quality bar), and the
statuses of [MAIL-US-B2](../USER_STORIES.md) and [MAIL-US-A4](../USER_STORIES.md).
