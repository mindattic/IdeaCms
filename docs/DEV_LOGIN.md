# Dev login on localhost — IdeaCms Admin

A developer signs in to Admin on localhost exactly the way production seeds its first admin: through the
MindAttic.Authentication **bootstrap token**, read from the uncommitted, machine-local Vault `Security`
bucket (`%APPDATA%\MindAttic\Security\providers.json`). There is no `.env` file, no dev auto-login, and
no hardcoded admin password. `Program.cs` already lists `"Security"` among the Vault buckets it loads.

## Steps

1. **Provision the dev Security bucket** (one-time per dev machine). Create
   `%APPDATA%\MindAttic\Security\providers.json`:
   ```jsonc
   {
     "pepper.v1":       "<base64 of 32 random bytes>",
     "bootstrap-token": "<a strong >=12-char string>",
     "reset-token-key": "<base64 of 32 random bytes>",
     "dp-kek":          "<base64 of 32 random bytes>"
   }
   ```
   Generate a 32-byte base64 value:
   ```powershell
   [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
   ```
   The file lives under `%APPDATA%`, outside the repo, so it cannot be committed.
2. **Run the app** (`dotnet run --project src/IdeaCms.Blazor`). On an empty database, startup
   migrates the auth schema, then the bootstrapper creates `admin` with a forced password change and
   `PasswordHash = Argon2id(bootstrap-token + pepper)`. Admin MFA is currently not required
   (`MindAttic:Auth:Mfa:RequireForAdmin = false`).
3. **Sign in** at `https://localhost:<port>/login` as `admin` / `<bootstrap-token>`.
4. You are redirected to `/account/change-password`. Set a real password (>=12 chars, HIBP-checked).
   After this the bootstrap token no longer signs anyone in. Seeding is a no-op once any user exists.

## Why this path

| Concern | bootstrap token |
|---|---|
| Second secret store | **No** — the same Vault `Security` bucket production uses |
| Fail-closed | **Yes** — no `bootstrap-token` ⇒ no seed; no `pepper.v1` ⇒ no start |
| Commit risk | **None** — the secret lives in `%APPDATA%`, outside the repo |
| Forced password change | **Honored** before first use |
| Dev/prod divergence | **None** — identical seed path; only the secret backend differs (file vs Key Vault) |

An uncommitted `.env` would add a second secret store, needs new parsing code, can be staged by
`git add -A`, and bypasses the forced change. A dev auto-login middleware bypasses the cookie pipeline
entirely. Neither is used.

## Reset for a clean first login

Drop the dev database (`IdeaCms` on LocalDB) so the next boot re-runs migrate → seed. Optionally
rotate the dev `bootstrap-token` in `providers.json` between runs.
