---
codex: 1
project: IdeaCms.Library
code: MAIL
layer: rfc
status: planned
updated: 2026-10-03
---

# RFC 0002 — UiUx raw-source → `.idea` build pipeline

## Problem
Components are authored as **raw js/css/html** in **MindAttic.UiUx** (the source-of-truth for
markup/styles). The shippable **`.idea`** projects live in the library (`library/`), which is their single
home; UiUx carries no `.idea` projects. The library's projects are **hand-maintained** — effectively a
second copy of what UiUx already holds — so the raw→`.idea` step is manual.

## Decision
Planned, not started: a generator that makes library projects from UiUx bundles.

## Sketch (not yet built)
A small generator (its **own repo/tool**, e.g. `IdeaCms.Forge`) that:
1. Reads a UiUx component bundle — its assets (`*.css`/`*.js`/`*.html`) + a `deps.json`/descriptor
   (kind = `Theme`|`Plugin`|`Component`, key, version, `uses[]`, asset list).
2. Emits the corresponding `IdeaCms.<Kind>.<Key>` project (or packs the `.idea` directly)
   into `library/` — namespace/`@inherits`/mount by convention, assets copied to
   `assets/`, so the output matches what's there now by hand.
3. Re-runs `ma-idea pack` + `verify` so `dist/` stays the single source of installable packages.

Net effect: UiUx stays raw source; the Library's `.idea`s become **generated, not duplicated**.

## What NOT to do
- Don't reintroduce `.idea` *projects* into UiUx — it stays raw source.
- Don't hand-edit generated Library projects once the pipeline exists (regenerate instead).

## Graduates into
A real tool + a library `docs/` note on "regenerate, don't hand-edit." Library `.idea` projects are
authored and maintained by hand.
