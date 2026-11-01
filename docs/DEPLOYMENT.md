# Deploying IdeaCms to Azure

One build of the CMS runs as **two deployments** on one App Service plan ([BIBLE §4.14](BIBLE.md#M.IDEA-§4.14)):

| Site | What it is |
|---|---|
| **https://mindattic.azurewebsites.net** | The company site: MindAttic's own content, on Ideas. |
| **https://mindattic-ideas-demo.azurewebsites.net** | A public, **vanilla** demo of Ideas: every first-party `.idea` installed, one hello-world page. **Wiped and re-provisioned every hour** with a new admin password, shown (behind a captcha) on the company site's Ideas page. |

Each is still a single deployment ([BIBLE §1](BIBLE.md#M.IDEA-§1)): pages go live by uploading a `.idea`,
not by redeploying. Deploy only when the engine itself changes.

Everything is **passwordless**. Each site has a system-assigned managed identity and reaches SQL, Blob
Storage and Key Vault through it. There is no SQL password, no storage key and no client secret in the
repo, in CI, or in app settings ([HOUSE-LAW-3](../../MindAttic.HouseRules.md#HOUSE-LAW-3)).

---

## What gets created

`infra/main.bicep` (with `infra/webapp.bicep` for each site):

| Resource | Company site | Demo |
|---|---|---|
| App Service plan (B1 Linux) | shared | shared |
| Web app + managed identity | `mindattic` | `mindattic-ideas-demo` |
| Azure SQL (Entra-only) | `MindAtticIdeas` | `MindAtticIdeasDemoTemplate` (pristine) → copied hourly to `MindAtticIdeasDemo` |
| Storage containers (private, no shared keys) | `media`, `dp-keys` | `demo-media`, `demo-dp-keys` |
| Key Vault (RBAC) | `kv-mindatticid-…`: auth secrets, `dp-protect`, `signing-cert-public`, `turnstile-secret` | `kv-demo-…`: its own auth secrets and `dp-protect`, `admin-password`, `credentials` |

**Isolation.** The demo identity has no role on anything of the company site's: not its database, vault,
or containers. The only reach across is one-way and named: the company site may **read** the demo vault's
`credentials` secret, which only CI writes. So nothing done on the demo — even with its admin login —
can touch the company site.

Roughly **$23–25/month** at the defaults (B1 ≈ $13 shared by both sites, three Basic databases ≈ $5 each,
storage and vaults are pennies).

---

## First-time setup

### 0. The GitHub OIDC principal

CI authenticates as the app registration **`gh-mindattic-ideas-deploy`** (Contributor on the resource
group) with federated credentials `repo:mindattic/IdeaCms:ref:refs/heads/main` (the branch was `master` until 2026-10-16; its credential is kept) and
`repo:mindattic/IdeaCms:environment:production`. Its ids are the repo secrets
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`. No other secret is needed.

### 1. Provision

```pwsh
az login
./infra/provision.ps1 -ResourceGroup rg-mindattic-ideas -WhatIf   # look first
./infra/provision.ps1 -ResourceGroup rg-mindattic-ideas
```

Re-runnable. After the Bicep it: seeds both vaults (CSPRNG, never printed; existing secrets left
alone); creates the SQL contained users **by object id** (company site read/write on `MindAtticIdeas`;
demo read/write on the template only; CI `db_ddladmin` + read/write on both); applies the schema to the
template; restarts the company site and dispatches the first demo reset.

### 2. Deploy

Push `main` (or `npm run deploy -- --app ideas` from MindAttic.Deploy). See below.

### 3. Turn on the demo-login reveal (Cloudflare Turnstile)

Until this is done the Ideas page shows the demo link and "Demo sign-in opens soon" — the login is
never shown without a human check.

1. In Cloudflare → Turnstile, add a widget for `mindattic.azurewebsites.net` (Managed mode).
2. Store the **secret key** in the company vault:
   `az keyvault secret set --vault-name kv-mindatticid-… --name turnstile-secret --value <secret>`
3. Re-run provisioning with the **site key** (public):
   `./infra/provision.ps1 -ResourceGroup rg-mindattic-ideas -TurnstileSiteKey <site key>`

### 4. Sign in to the company site

If its database has no users yet:
`az keyvault secret show --vault-name kv-mindatticid-… --name bootstrap-token --query value -o tsv` —
sign in as `admin`, you are forced to change it, then **rotate the Key Vault secret**.

---

## Continuous deployment — `.github/workflows/azure-deploy.yml`

On push to `master` and on dispatch:

1. **build** — restore, build, full NUnit suite (a red test stops everything), publish, generate
   `seed/demo.idealist` from the packages actually in the build, emit the idempotent migration script.
2. **migrate** — apply it to `MindAtticIdeas` **and** the demo template, under an Entra token through a
   single-run firewall rule. Skippable with `skip_migrate`.
3. **deploy-company** — deploy the artifact to the company site, restart it onto it (a zip deploy
   overwrites DLLs under the running process on Linux), then wait (up to 20 minutes) until `/_health`
   reports **this commit** ready (`X-IdeaCms-Version` contains the SHA, `X-IdeaCms-Ready: true`) and `/`
   answers 200. The build stamps the commit with `-p:SourceRevisionId`; a bare 200 could come from the
   container being replaced.
4. **deploy-demo** — only after the company job finishes (whatever its result): the same artifact to the
   demo, restarted onto it. The two sites never cold-start together on the plan's single core.
5. **reset-demo** — run the demo reset, so the demo never runs new code on an old schema.

Never deploys when migrate *ran and failed*: that is how you get a half-migrated database.

### How a site starts

The server listens **before** the boot sequence (discovery, seed, admin bootstrap, `.idea`/`.idealist`
provisioning) runs, so a container answers within seconds of `dotnet` starting:

- **`/_health`** — liveness, always 200, never touches the database. Headers: `X-IdeaCms-Ready`
  (`false` until the boot sequence finishes) and `X-IdeaCms-Version` (the build, `+<commit>`).
- **Everything else** — `503 starting` with `Retry-After: 10` until the boot sequence finishes.
- **A transient failure** in the boot sequence (a SQL, socket, timeout or Azure identity error) is retried:
  8 attempts, 5 s doubling to 60 s apart, about four minutes in all. Every step is idempotent.
- **A permanent failure** (a missing setting, a bad `.idealist`, retries spent) logs
  `[startup] initialisation FAILED` and exits with code **1**; App Service restarts the container.

On the shared B1 plan a warm restart of the company site takes about two minutes, most of it the
platform's container preamble (certificate rehash, Oryx); the app's own boot is under a minute.

## The hourly demo reset — `.github/workflows/demo-reset.yml`

Hourly (`cron: '0 * * * *'`), after every deploy, and on dispatch. It is the demo's operator; the
product contains no reset code ([M.IDEA-LAW-11](BIBLE.md#M.IDEA-LAW-11)).

1. `credentials` := `{"status":"resetting"}` — the Ideas page says the demo is resetting.
2. A new password (CSPRNG, 4×5 unambiguous characters) → demo vault `admin-password`. Masked in logs,
   passed only through files.
3. **Stop** the demo, then pin its bootstrap-token setting to the new secret **version** (an unversioned
   Key Vault reference can be served from App Service's cache). A settings change does not start a
   stopped site.
4. Delete `MindAtticIdeasDemo`; copy `MindAtticIdeasDemoTemplate` to it (schema + the demo identity's
   user, no content). Empty `demo-media`.
5. **Start** the demo. Its one boot against the empty database runs `seed/demo.idealist` (every package +
   the hello page) and creates `admin` from the new password with no forced change
   (`MindAttic:Auth:Bootstrap:RequirePasswordChange=false`). Sessions revalidate every 15 s and cap at
   1 h, so the previous hour's sessions die immediately.
6. Wait for `/_health` and `/`, then **sign in for real** with the new password (the log prints where the
   login redirected).
7. Only then `credentials` := `{status: ready, url, username, password, validUntilUtc}`.

The order is the contract (`DeploymentPackagingTests.DemoResetStopsTheDemoAndPinsTheNewPasswordBeforeReplacingItsDatabase`).
`admin` is seeded once, by the first boot that finds no users, from the token that boot started with. With
the demo running, a boot between the database copy and the token change seeded the previous hour's
password, and App Service kept serving the old container while the new one warmed up; stopped, the only
boot that can see the empty database already has the new token.

Any failure leaves `credentials` at `resetting`: the page never shows a login that does not work. A run
that fails between the stop and the start leaves the demo stopped; the next run starts it.
GitHub's cron can start a few minutes late; the displayed login is always the live one. Expect a few
minutes of demo downtime at the top of each hour.

**How the company site shows it.** `IdeaCmsBrochure` asks the host's `IDemoAccess` feature for the URL and
status only. The login is fetched by the browser from `POST /_demo/reveal` with a Turnstile token, which
the server verifies with Cloudflare (bound to the `demo-reveal` action), rate-limits per client IP
(5/minute), and answers `Cache-Control: no-store`. The password is never in page HTML or logs.

---

## The NuGet problem, and why `lib/local-packages/` is in git

Ideas references six private packages — `MindAttic.Vault` (V3+), `MindAttic.Legion`,
`MindAttic.Authentication`, `MindAttic.Media`, `MindAttic.Media.Azure` and
`MindAttic.Ideas.Page.Frontpage`. On a dev box those come from `C:\LocalNuGet` and `..\local-feed`.
**A GitHub runner has neither**, and NuGet tolerates a missing local source *silently* — so without
vendored copies the restore fails with a confusing `NU1101` about a package that plainly exists.

`lib/local-packages/` holds a git-tracked copy of each, and `nuget.config` lists it first so a dev
box and CI resolve identically. `.gitignore` excludes `*.nupkg` globally and re-includes this folder.

**When you bump a MindAttic package:** drop the new `.nupkg` in `lib/local-packages/` *and* update
the `PackageReference`. Old versions can stay; NuGet picks the one the csproj asks for.

---

## Configuration reference

Set as App Service application settings. `__` maps to `:` in the config chain.

| Setting | Value | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Turns off `MigrateAsync`, the dropbox auto-install, and the dev auth bypass. |
| `ConnectionStrings__IdeaCms` | `Server=tcp:…;Authentication=Active Directory Default;…` | No password. |
| `DataProtection__BlobUri` | `https://….blob.core.windows.net/dp-keys/ideas-keys.xml` | **Required in production** — the app throws without it. |
| `DataProtection__KeyVaultKeyId` | `https://….vault.azure.net/keys/dp-protect/<version>` | **Required in production.** |
| `Media__Provider` | `azure` | `local` keeps assets on the App Service filesystem, which does not survive a redeploy. |
| `Media__Azure__BlobServiceUri` | `https://….blob.core.windows.net/` | |
| `Media__Azure__ContainerName` | `media` | |
| `Media__Azure__SignedUrlMinutes` | `60` | SAS lifetime for `/_media/{uid}` redirects. |
| `MindAttic__Vault__Security__pepperv1` | Key Vault reference | Argon2id pepper (`pepper.v1`). |
| `MindAttic__Vault__Security__bootstraptoken` | Key Vault reference | First-admin seed; rotate after use. |
| `MindAttic__Vault__Security__resettokenkey` | Key Vault reference | Password-reset token signing key. |
| `MindAttic__Vault__Security__dpkek` | Key Vault reference | |
| `MindAttic__Vault__PackageSigning__signingcertpublic` | Key Vault reference | Public half of the package-signing cert. Without it **every** `.idea` install fails closed. |

---

## Getting content in

- **A page or widget** — upload the `.idea` through Admin. No redeploy ([BIBLE §1](BIBLE.md#M.IDEA-§1)).
- **An image** — Admin → Media.
- **A video, or anything large** — `--upload-media`, which streams from disk straight into blob
  storage instead of crossing a SignalR circuit:
  ```pwsh
  dotnet run --project src/IdeaCms.Blazor -- --upload-media .\feature.mp4 --folder site
  ```
  Point it at production by exporting the same `Media__*` and `ConnectionStrings__IdeaCms` values
  locally. `/_media/{uid}` then 302s to a short-lived SAS and Azure serves the Range requests
  ([BIBLE §4.11](BIBLE.md#M.IDEA-§4.11)).
- **A whole authored site** — export it where you built it, import it where it should live
  ([BIBLE §4.9](BIBLE.md#M.IDEA-§4.9)). This is the only path that
  carries hand-curation: composed page bodies, extracted media and the `ComponentMetadata` rows behind
  `frommd`/`fromhtml` slots — plus, for a fresh deployment, which `.idea` packages it should install.
  ```pwsh
  # on the source (your dev box)
  dotnet run --project src/IdeaCms.Blazor -- --export-idealist D:	emp\site.idealist

  # against production — dry run first; it prints exactly what it would create and update
  $env:ConnectionStrings__IdeaCms = '<production connection string>'
  dotnet run --project src/IdeaCms.Blazor -- --import-idealist D:	emp\site.idealist --dry-run
  dotnet run --project src/IdeaCms.Blazor -- --import-idealist D:	emp\site.idealist
  ```
  Safe to re-run: pages reconcile on uid then slug (so the baseline seed's pages are **adopted**, not
  duplicated) and media is matched by SHA-256 (so nothing re-uploads). The import reports how many
  pages carry `Author` trust — raw, unsanitized markup — and `--untrusted` refuses it.

---

## Troubleshooting

**App returns 500 immediately after deploy.** Almost always a missing required setting. Check the
log stream: `az webapp log tail -g rg-mindattic-ideas -n mindattic` (or `-n mindattic-ideas-demo`). `DataProtection:BlobUri`
and `DataProtection:KeyVaultKeyId` throw by name; a missing Security secret throws
`Required auth secret '<name>' was not found`.

**`/_health` is 200 but every page 404s.** The catalog seeded but no pages exist — expected on a
brand-new database until you install content.

**Media 404s or 403s.** Confirm the app identity still holds Storage Blob Data Contributor. A SAS
is user-delegation signed, which needs that role; without it the signer declines and the endpoint
falls back to streaming, which then finds no bytes.

**Migrate job cannot reach SQL.** The firewall rule is per-run and torn down in an `always()` step.
If a run was killed mid-flight, delete the leftover `gh-<runid>` rule on the SQL server.

**`az webapp deploy` reports failure but the site is fine.** The CLI stops polling at ten minutes;
first boot installs 53 `.idea`s against a 5-DTU database and takes longer. Trust `/_health`, not the
CLI's verdict — the template sets `WEBSITES_CONTAINER_START_TIME_LIMIT=1800` so the container itself
is allowed to finish.

**Deployment 400s with rsync "Invalid argument" errors.** The zip was built by PowerShell's
`Compress-Archive`, which writes `\` path separators that Linux cannot unpack. Build the package with
forward slashes (`dotnet publish` then a zip tool that uses `/`).

**A secret you definitely set is "not found" on Linux.** App Service rewrites application-setting
names when injecting them as environment variables: hyphens are dropped and dots become underscores,
so `…Security__pepper.v1` arrives as `…Security__pepper_v1`. MindAttic.Authentication V4 matches
these by reducing both sides to letters and digits ([BIBLE §4.14](BIBLE.md#M.IDEA-§4.14)). Azure now also **rejects** hyphenated names
outright (`AppSetting with name '…' is not allowed`), which blocks every settings update — so every
setting here is alphanumeric.

**Every `.idea` install fails with "No trusted package-signing certificate".** The
`MindAttic__Vault__PackageSigning__signingcertpublic` setting (Key Vault secret `signing-cert-public`)
is missing. And always pack with `pack-all.ps1 -Sign` — an unsigned package is refused on every path.

**Container exits with code 134 during startup, over and over.** 134 is SIGABRT: .NET aborts on an
unhandled exception (and writes a core dump, which itself takes ~40 s on B1). The app catches every
boot-sequence failure and exits 1, so a 134 now means something escaped *before* the boot sequence — host
construction or configuration (see the next entry). To see it, turn container logging on, read
`LogFiles/<date>_<instance>_default_docker.log` through Kudu, and turn it off again:
`az webapp log config -g rg-mindattic-ideas -n mindattic --docker-container-logging filesystem` …
`--docker-container-logging off`. Plan-level CPU at 100% for minutes means both sites are cold-starting
together; the deploy no longer does that, but a manual restart of both, or an hourly demo reset during a
company restart, still can.

**`[startup] attempt n/8 failed with a transient SqlException`.** An Entra-authenticated SQL login was
reset mid-handshake ("A connection was successfully established with the server, but then an error
occurred during the login process", `Connection reset by peer`). It happens while a cold container is
fighting the other site for the CPU and the managed-identity token is slow to arrive. The retry covers it;
if every attempt fails, check that the site's identity is still a contained user on its database.

**App aborts at startup with a stack trace inside `ConfigurationBuilder`.** MindAttic.Vault below V3
throws when the host has no user profile, which on Linux is during host construction — SIGABRT before
any application code runs. Use MindAttic.Vault V3 or later, which resolves a root on every OS.
