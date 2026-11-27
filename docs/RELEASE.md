---
layer: release
status: living
---
# IdeaCms — Release charter

> The one finish line this repo is working toward. Every open ticket in this repo sits under one of the epics
> below, and every epic is part of the milestone. Work that does not fit is out of scope until this milestone is
> reached (owner, 2026-11-27, MAT-T319).

## 1. What this is
A single-deployment Blazor CMS for .NET 10: upload a signed `.idea` package and a page, theme, plugin or component
goes live at once, with no redeploy and no app restart. It is the owner's own CMS, run as an internal app.

## 2. The milestone: IdeaCms local 1.0 {#milestone}
- **Milestone:** MS-9, "IdeaCms local 1.0"
- **Published means:** internal tool on `R:\MindAttic\Apps\IdeaCms\Debug` and `\Release`, deployed by the shared
  run menu (`run.ps1` / `run.bat`), against local SQL Server.
- **The natural stopping point:** Azure was decommissioned on 2026-11-26 (MAT-T297), so the web apps, Azure SQL,
  Blob, Key Vault and the public demo are gone. The CMS itself is feature-complete (USER_STORIES: 77 done). The
  finish line is that it builds, deploys and runs from R: against local SQL with every Azure dependency removed,
  its known visible bugs fixed, and its docs describing that reality. Nothing new is added to the product.

## 3. Done when (acceptance checks)
1. build — `IdeaCms.slnx` builds clean on main.
2. exists — `R:\MindAttic\Apps\IdeaCms\Release` holds the deployed IdeaCms.Blazor build (and `\Debug`).
3. signoff — the owner runs Release from the menu, signs in at `/login`, edits a page in `/admin` and uploads a `.idea`.

Also expected (reviewed by hand, not a tracked check): no Azure SQL / Key Vault / Blob default left in `src/`,
`tools/` or `infra/` (no `database.windows.net` default connection, no `infra/provision.ps1`).

## 4. Epics, in priority order
| Priority | Epic (ticket) | What it delivers | Features |
|---|---|---|---|
| 900 | IDEA-T94 Local-only runtime | Builds, deploys and runs from R: on local SQL, Azure code gone | IDEA-T97 Local deploy on R: (990), IDEA-T98 Local SQL Server (980), IDEA-T99 Azure code removal (970) |
| 800 | IDEA-T95 Known bug fixes | The open real bugs in shipped code | IDEA-T100 Page rendering fixes (890), IDEA-T101 URL safety (880) |
| 700 | IDEA-T96 Docs match reality | README and deployment docs describe the local app | IDEA-T102 README refresh (790), IDEA-T103 Deployment docs (780) |

Tasks: IDEA-T97 = IDEA-T89 then IDEA-T90; IDEA-T98 = IDEA-T104; IDEA-T99 = IDEA-T88; IDEA-T100 = IDEA-T38,
IDEA-T49; IDEA-T101 = IDEA-T7; IDEA-T102 = IDEA-T20; IDEA-T103 = IDEA-T105.

## 5. Out of scope (not before this milestone)
- Public hosting anywhere (Hetzner move MAT-T246 / IDEA-T75..T77, any Azure redeploy) — the app is local-only now;
  a hosting move is its own future milestone.
- The public hourly-reset demo and its Key Vault demo credentials / Turnstile reveal — the demo deployment is gone.
- Broad unit-test backfill for untested classes (the 2026-10-14 coverage audit) — nice-to-have, not a finish line.
- Open-ended sweeps: exhaustive bug hunts, "/simplify" cleanups, "run the whole suite until green" loops.
- New citizens, new admin panels, new package formats.

## 6. Current verified state
- Shared run menu adopted: `run.ps1` on `Invoke-AppRun` deploys to `R:\MindAttic\Apps\IdeaCms` (a042c62, MAT-T306).
- Development config points at local SQL Server (8adcf64, IDEA-T79); nuget restore uses only vendored packages (a0cd8cd).
- MindAttic.Storage 1.0.0 replaced MindAttic.Media (f84c0ac); Vault 7.0.0 and Log 3.0.0 vendored (5e1c0ea, 48ef31d).
- Recent bug fixes landed: IDEA-T78..T87 (packer settings, forwarded headers, PageHost admin slug, seeders keep
  stored content, TabBoard z-index).
- Not yet true: `R:\MindAttic\Apps\IdeaCms` has not been deployed; `C:\Apps\Ideas` still exists;
  `CmsDbContextFactory` and `tools/*.ps1` still default to Azure SQL; `infra/` still holds Azure provisioning.

## 7. Decision record
- 2026-11-27 — charter drafted from README/BIBLE/docs/tickets; Legion vote: Approve, 9/9 voters (plurality quorum
  reached, consensus 1.0), no dissent.
- Triage: 34 open (excluding the job ticket IDEA-T93) → done 1 (IDEA-T25), duplicate 0, wont-do 26 (test-coverage
  backfill, demo/Key Vault, open-ended sweeps, Hetzner move IDEA-T75..T77), kept 7 (IDEA-T7 and IDEA-T49 reworded),
  new tasks 2 (IDEA-T104, IDEA-T105).
