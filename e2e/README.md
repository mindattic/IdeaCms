# IdeaCms — Cypress E2E

End-to-end coverage of the core admin flow:

> admin logs in → opens admin → **uploads a compiled `.idea`** → **creates a page**
> that references it by a `{{tag}}` → the page **renders the content** (no missing-content placeholder).

Spec: [`cypress/e2e/admin-widget-flow.cy.js`](cypress/e2e/admin-widget-flow.cy.js).  
Fixture: `cypress/fixtures/IdeaCms.Plugin.Tooltip.V1.idea` (packed from the first-party library).

## Prerequisites

- Node.js (for Cypress)
- A running instance of `IdeaCms.Web` (these tests do **not** start the app)

## Run

```pwsh
# 1) Start the CMS on a dev port + separate DB (from the repo root)
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS        = "https://localhost:7207"
$env:ConnectionStrings__IdeaCms = "Server=(localdb)\MSSQLLocalDB;Database=IdeaCms_E2E;Trusted_Connection=True;TrustServerCertificate=True"
dotnet run --project src/IdeaCms.Web
# On first run the bundled library/*.idea files seed into the catalog automatically.

# 2) In a separate shell, from this e2e/ folder:
npm install
$env:CYPRESS_BASE_URL       = "https://localhost:7207"
$env:CYPRESS_ADMIN_PASSWORD = "<bootstrap admin password from Vault Security:bootstrap-token>"
npm run cy:run    # headless
npm run cy:open   # interactive
```

## Environment variables

| Variable | Default | Description |
|---|---|---|
| `CYPRESS_BASE_URL` | `https://localhost:7207` | URL of the running CMS |
| `CYPRESS_ADMIN_USER` | `admin` | Admin username |
| `CYPRESS_ADMIN_PASSWORD` | _(required)_ | Admin password |
| `CYPRESS_LOGIN_PATH` | `/account/login` | Login page path |

## Notes

- The bootstrap admin is seeded with `MustChangePassword`. Complete the password change once (or seed a known password) before running the suite so the login command can authenticate cleanly.
- Selectors use labels and button text, not fragile class names. If the admin markup changes materially, update `cypress/support/commands.js` and the spec.
- `chromeWebSecurity: false` is set to allow the dev TLS / self-signed cert.
