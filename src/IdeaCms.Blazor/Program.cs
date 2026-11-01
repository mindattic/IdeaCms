﻿using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication;
using MindAttic.Authentication.Web;
using IdeaCms.Core.Data;
using IdeaCms.Core.DependencyInjection;
using IdeaCms.Core.Discovery;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Services;
using IdeaCms.Blazor.Cli;
using IdeaCms.Blazor.Components;
using IdeaCms.Blazor.Services;
using MindAttic.Legion;
using MindAttic.Media;
using MindAttic.Vault.Configuration;
using MindAttic.Vault.DependencyInjection;

// ---- CLI mode: --expand-css-shorthand -------------------------------------------------------
// Pure filesystem pass (M.IDEA-§4.7) -- runs before any Vault/DB wiring since it needs no
// configured secrets and no running app; rewrites library/**/*.css in place via CssConflictMerger.
// dotnet run --project src/IdeaCms.Blazor -- --expand-css-shorthand [--path library] [--dry-run]
if (args.Contains("--expand-css-shorthand"))
{
    Environment.Exit(await ExpandCssShorthandCli.RunAsync(args));
}

var builder = WebApplication.CreateBuilder(args);

// --- MindAttic.Vault settings (LAMP): IdeaCms' app-level settings are edited in MindAttic.Vault.Desktop and projected
// onto the configuration keys the app reads, below environment variables (App Service settings still win). ---
var ideaCmsSettings = IdeaCmsSettings.Load(builder.Configuration);
if (args.Contains(MindAttic.Vault.Settings.Manifest.SettingsManifestCommand.Switch, StringComparer.OrdinalIgnoreCase)) return;
ideaCmsSettings.AddTo(builder.Configuration);

// --- MindAttic.Vault: all credentials/config flow through the Vault chain (HOUSE-LAW-3). No User Secrets. ---
// "Security" is the MindAttic.Authentication trust domain (pepper, bootstrap-token, reset-token-key);
// it is NOT in the default bucket list, so it must be named explicitly or the auth secrets at
// %APPDATA%\MindAttic\Security\providers.json never bind and AuthBootstrapper fail-closes.
builder.Configuration
    .AddMindAtticVaultFiles(o => o.Buckets = new[]
    {
        "LLM", "Brokers", "Tokens", "Subtitles", "Notifications", "AudioStore", "Security", "Media", "PackageSigning",
    })
    .AddEnvironmentVariables();
builder.Services.AddMindAtticVault(builder.Configuration);

// Connection string resolves through config/env (Vault-compatible); LocalDB fallback for dev.
var connectionString =
    builder.Configuration.GetConnectionString("IdeaCms")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__IdeaCms")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=IdeaCms;Trusted_Connection=True;TrustServerCertificate=True";

// --- CMS Core: EF, discovery over this assembly's citizens + referenced Idea RCLs, catalog, gate,
//     seed. The MindAttic front page ships as a compiled Page Idea (an RCL NuGet). ---
builder.Services.AddIdeaCms(
    connectionString,
    typeof(Program).Assembly,
    typeof(MindAttic.Ideas.Page.Frontpage.V1).Assembly);

// --- Media store (M.IDEA-§4.11): local disk by default, Azure Blob when Media:Provider=azure. AddIdeaCms
//     already registered the local store; the Azure registration replaces it. The page-facing contract
//     is /_media/{uid} either way, so switching the backing store changes no page markup. Blob-backed
//     media serves via a short-lived SAS redirect, which is what gives video working Range/seek. ---
var mediaRoot = Path.Combine(builder.Environment.ContentRootPath, "media");
builder.Services.AddConfiguredMediaStore<CmsDbContext>(builder.Configuration, mediaRoot);

// --- MindAttic.Legion: LLM + voting (HOUSE-LAW-4). Zero-config; keys resolve via Vault when used. ---
builder.Services.AddLegionClient();

// --- Blazor (global InteractiveServer available; auth pages stay static SSR). ---
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpContextAccessor();

// --- Auth: the unified, Vault-backed MindAttic.Authentication engine (M.IDEA-LAW-9),
//     replacing the interim cookie/AuthService stack. It registers the cookie schemes, MaPolicies.Admin,
//     Data Protection, cascading auth state + a revalidating provider, and all auth services over
//     CmsDbContext (the IAuthDataContext). MFA is off for now ⇒ MaPolicies.Admin is role-only. ---
builder.Services.AddMindAtticAuthentication<CmsDbContext>(builder.Configuration, o =>
{
    o.AppName = "IdeaCms";                                 // per-app Data Protection trust boundary (no cross-app SSO)
    o.IsProduction = !builder.Environment.IsDevelopment();
    // Keep the IdeaCms-owned policies working on the library's principal. Neither name is the canonical
    // ma:admin, so this composes with the library's own MaPolicies.Admin registration.
    o.ConfigureAdditionalPolicies = authz =>
    {
        authz.AddPolicy("Admin", p => p.RequireRole(MaRoles.Admin));
        authz.AddPolicy("AuthorRawMarkup", p => p.RequireClaim(CmsClaims.AuthorRawMarkup));
    };
    if (o.IsProduction)
    {
        // PROD: persist + protect the Data Protection key ring (the library fail-closes if absent in prod).
        o.ConfigureDataProtection = dp =>
        {
            var cred = new Azure.Identity.DefaultAzureCredential();
            var blobUri = builder.Configuration["DataProtection:BlobUri"]
                ?? throw new InvalidOperationException("DataProtection:BlobUri is required in production.");
            var kvKeyId = builder.Configuration["DataProtection:KeyVaultKeyId"]
                ?? throw new InvalidOperationException("DataProtection:KeyVaultKeyId is required in production.");
            dp.PersistKeysToAzureBlobStorage(new Uri(blobUri), cred)
              .ProtectKeysWithAzureKeyVault(new Uri(kvKeyId), cred);
        };
    }
    // DEV: the library persists the key ring to %APPDATA%\MindAttic\DataProtection\IdeaCms.
});

// Re-emit the IdeaCms-owned CmsClaims.AuthorRawMarkup claim at sign-in for trusted authors (Admins).
builder.Services.AddScoped<IMaClaimsAugmentor, IdeaCmsClaimsAugmentor>();
builder.Services.AddScoped<InstanceClipboard>();

// The public demo this site advertises (a SEPARATE deployment, M.IDEA-LAW-11). Inert unless Demo:Url and
// Demo:KeyVaultUri are configured; the login reveal additionally needs both Turnstile keys.
IdeaCms.Blazor.Demo.DemoReveal.AddDemoAccess(builder.Services, builder.Configuration);

var app = builder.Build();

// --- Startup: migrate -> discover citizens -> seed CMS content -> bootstrap admin -> provision. ---
// MigrateAsync is dev-only (prod runs DDL in the CI migrate job under db_ddladmin). AuthBootstrapper
// seeds 'admin' from the Vault Security:bootstrap-token (MustChangePassword) and no-ops once a user exists.
// Every step is idempotent, so StartupGate can retry the whole sequence after a transient failure.
// In server mode it runs AFTER the server is listening (see the end of this file); /_health answers at
// once and every other request is 503 "starting" until it completes (M.IDEA-§4.14).
var readiness = new IdeaCms.Blazor.StartupReadiness();
async Task InitializeAsync(CancellationToken ct)
{
    using var scope = app.Services.CreateScope();
    var sp = scope.ServiceProvider;
    if (app.Environment.IsDevelopment())
    {
        await sp.GetRequiredService<CmsDbContext>().Database.MigrateAsync(ct);
        // Phase 2 (MindAttic.Log 3.0.0): one-time copy of Phase 1's dbo.MindAttic_Log rows (if any
        // — most environments never ran the old CreateTableSqlServer dev step at all) into the new
        // standalone database, then drop that table. Idempotent and a clean no-op on every later
        // startup. Dev-only for now, same convention as the EF migration immediately above ("prod
        // DDL runs in CI") — a production environment with real historical rows needs this run
        // through that same CI/ops path, not silently on every app boot.
        await MindAttic.Log.Migration.MindAtticLogMigrator.MigrateFromSqlServerAsync(
            connectionString, MindAttic.Vault.Paths.VaultPaths.LoggingDbPath("IdeaCms"), dropSource: true, ct);
    }
    await sp.GetRequiredService<DiscoveryService>().RunAsync(ct);
    await sp.GetRequiredService<SeedService>().SeedAsync(ct);
    await IdeaCms.Blazor.AdminBootstrap.ApplyAsync(builder.Configuration,
        sp.GetRequiredService<MindAttic.Authentication.Services.IUserStore>(),
        sp.GetRequiredService<MindAttic.Authentication.Services.IUserAdminService>(),
        sp.GetRequiredService<MindAttic.Authentication.Secrets.IAuthSecrets>(),
        c => sp.GetRequiredService<MindAttic.Authentication.Services.AuthBootstrapper>()
                .SeedAdminAsync(IdeaCms.Blazor.AdminBootstrap.AdminUserName, c));

    // VANILLA vs. CUSTOM INSTANCE (M.IDEA-§4.9): absent IdeaCms:Idealist/IDEACMS_IDEALIST installs every
    // first-party .idea physically present in ./library, best-effort, exactly as this codebase always
    // did. A configured .idealist is applied instead — packages, then pages — and any failure aborts
    // startup rather than leaving a curated instance half-provisioned.
    await IdeaCms.Blazor.BootProvisioning.ApplyAsync(sp, app.Environment.ContentRootPath, builder.Configuration, ct: ct);

    // DEV convenience: auto-install every .idea dropped in the IDEAS_DROPBOX folder through the REAL
    // install path (IPackageInstallService = the admin-upload code path), idempotent + allowOverride.
    // Lets you stage a folder of packages and see them compose on a live page with no manual upload.
    if (app.Environment.IsDevelopment()
        && Environment.GetEnvironmentVariable("IDEAS_DROPBOX") is { Length: > 0 } dropbox
        && Directory.Exists(dropbox))
    {
        var installer = sp.GetRequiredService<IdeaCms.Core.Services.IPackageInstallService>();
        foreach (var file in Directory.EnumerateFiles(dropbox, "*.idea").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                await using var bytes = File.OpenRead(file);
                var plan = await installer.InstallAsync(bytes, allowOverride: true, ct);
                Console.WriteLine($"[dropbox] {Path.GetFileName(file)} -> {plan.Action}");
            }
            catch (Exception ex) { Console.Error.WriteLine($"[dropbox] {Path.GetFileName(file)} FAILED: {ex.Message}"); }
        }
    }
}

// First in the pipeline: nothing but /_health is served until initialisation has finished.
app.Use(next => IdeaCms.Blazor.StartupGate.Gate(readiness, next));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Honor the reverse proxy's forwarded scheme/IP (secure cookie + real client IP) before auth.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);

// authn + authz + forced-step (MustChangePassword → /account/change-password) + scoped CSP on the auth surface.
app.UseMindAtticAuthentication();
app.UseAntiforgery();
IdeaCms.Blazor.Demo.DemoReveal.MapDemoReveal(app);

app.MapStaticAssets();
// Runtime package assets: /_ideas/{category}/{key}/{version}/{**path} -> the package's extracted wwwroot
// (category-qualified so a Page and a Component can share a key). ResolveAsset guards path traversal.
var assetContentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
app.MapGet("/_ideas/{category}/{key}/{version:int}/{**path}",
    IResult (string category, string key, int version, string path, IPackageExtractor extractor) =>
    {
        var file = extractor.ResolveAsset(category, key, version, path);
        if (file is null) return Results.NotFound();
        var contentType = assetContentTypes.TryGetContentType(file, out var ct) ? ct : "application/octet-stream";
        return Results.File(File.OpenRead(file), contentType);
    });
// Admin-only: download the raw .idea blob (for rollback / re-share). Auth guard is enforced by RequireAuthorization.
app.MapGet("/_ideas/packages/{category}/{key}/{version:int}",
    async (string category, string key, int version, IPackageBlobStore blobs, CancellationToken ct) =>
    {
        var blobPath = LocalFilePackageBlobStore.BlobPathFor(category, key, version);
        var stream = await blobs.OpenAsync(blobPath, ct);
        if (stream is null) return Results.NotFound();
        var filename = $"{key}.V{version}.idea";
        return Results.File(stream, "application/octet-stream", filename);
    }).RequireAuthorization("Admin");
app.MapGet("/_ideas/{*path}", () => Results.NotFound());   // anything else under /_ideas

// Media assets: /_media/{uid:guid} redirects to a signed blob URL when the backend can mint one,
// otherwise streams the payload with Range support (inline for image/text/video/audio/PDF).
app.MapMediaEndpoints();

// Liveness probe for the App Service health check. Deliberately does NOT touch the database: App
// Service restarts an instance that fails this, and a transient SQL blip must not turn into a
// restart loop. Under `/_` like every other reserved route, so it can never shadow a page slug.
// Answers as soon as the server listens; X-IdeaCms-Ready / X-IdeaCms-Version report init state and build.
app.MapGet(IdeaCms.Blazor.StartupGate.HealthPath,
    (HttpContext ctx) => IdeaCms.Blazor.StartupGate.Health(ctx, readiness)).AllowAnonymous();
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   // PageHost (the catch-all "/{*Slug}" content route) lives in the IdeaCms.Rendering RCL.
   // .NET 8+ endpoint-based routing only discovers routable components from App's assembly unless the
   // RCL is registered here — the <Router AdditionalAssemblies> alone does NOT register server endpoints,
   // so without this every content page 404s.
   .AddAdditionalAssemblies(typeof(IdeaCms.Rendering.PageHost).Assembly);

// MindAttic.Authentication HTTP endpoints — /_ma-auth/{login,mfa-challenge,logout,change-password,reset/*}.
app.MapMindAtticAuthEndpoints();

// ---- CLI modes run the boot sequence first, in the foreground, then their command and exit. ----
string[] cliFlags = ["--install", "--extract-media", "--upload-media", "--export-idealist", "--import-idealist", "--compose-idealist", "--seed"];
var cliMode = args.Any(a => cliFlags.Contains(a, StringComparer.Ordinal));
if (cliMode)
{
    await IdeaCms.Blazor.StartupGate.RunWithRetryAsync(InitializeAsync,
        IdeaCms.Blazor.StartupRetryPolicy.Default, Console.Error);
    readiness.MarkReady();
}

// ---- CLI mode: --install <file.idea> --------------------------------------------------------
// dotnet run --project src/IdeaCms.Blazor -- --install path/to/Foo.V1.idea
var installIdx = Array.IndexOf(args, "--install");
if (installIdx >= 0)
{
    var ideaPath = installIdx + 1 < args.Length ? args[installIdx + 1] : null;
    if (string.IsNullOrEmpty(ideaPath) || !File.Exists(ideaPath))
    {
        Console.Error.WriteLine($"[install] File not found: {ideaPath ?? "(none)"}");
        Environment.Exit(1);
    }
    using var cliScope = app.Services.CreateScope();
    var installer = cliScope.ServiceProvider.GetRequiredService<IdeaCms.Core.Services.IPackageInstallService>();
    await using var bytes = File.OpenRead(ideaPath);
    var plan = await installer.InstallAsync(bytes, allowOverride: true);
    Console.WriteLine($"[install] {Path.GetFileName(ideaPath)} -> {plan.Action}");
    Environment.Exit(0);
}

// ---- CLI mode: --extract-media -------------------------------------------------------------
// Lift inline base64 images out of page bodies into the managed media store.
// dotnet run --project src/IdeaCms.Blazor -- --extract-media [--slug frontpage] [--folder site] [--dry-run]
if (args.Contains("--extract-media"))
{
    var exit = await ExtractMediaCli.RunAsync(args, app.Services);
    Environment.Exit(exit);
}

// ---- CLI mode: --upload-media ---------------------------------------------------------------
// Stream local files straight into the configured media store — the path for anything too large to
// push through the Admin panel's browser circuit (video above all).
// dotnet run --project src/IdeaCms.Blazor -- --upload-media <file…> [--folder site] [--media-type video] [--dry-run]
if (args.Contains("--upload-media"))
{
    var exit = await UploadMediaCli.RunAsync(args, app.Services);
    Environment.Exit(exit);
}

// ---- CLI mode: --export-idealist / --import-idealist / --compose-idealist --------------------
// Move AUTHORED CONTENT between environments and/or describe a deployment's provisioning (M.IDEA-§4.9). A .idea package carries a citizen; an idealist carries
// what an author built with citizens — pages, settings, per-component metadata, media — PLUS the
// Packages[] list of citizens a fresh instance should install. This is the only path that reproduces
// hand-curation, which --seed regenerates the shape of but not the substance of.
// dotnet run --project src/IdeaCms.Blazor -- --export-idealist site.idealist [--site key] [--slug projects/] [--no-media] [--dry-run]
// dotnet run --project src/IdeaCms.Blazor -- --import-idealist site.idealist [--into-site key] [--dry-run] [--untrusted] [--prune] [--packages-dir dir]
// dotnet run --project src/IdeaCms.Blazor -- --compose-idealist provision.idealist --package Theme.cyberspace@1 [--from-site key] [--dry-run]
if (args.Contains("--export-idealist"))
{
    var exit = await ExportIdeaListCli.RunAsync(args, app.Services);
    Environment.Exit(exit);
}
if (args.Contains("--import-idealist"))
{
    var exit = await ImportIdeaListCli.RunAsync(args, app.Services);
    Environment.Exit(exit);
}
if (args.Contains("--compose-idealist"))
{
    var exit = await ComposeIdeaListCli.RunAsync(args, app.Services);
    Environment.Exit(exit);
}

// ---- CLI mode: --seed <target> --------------------------------------------------------------
// dotnet run --project src/IdeaCms.Blazor -- --seed core        (re-run SeedService migrations)
// dotnet run --project src/IdeaCms.Blazor -- --seed from-html [--dry-run]
// dotnet run --project src/IdeaCms.Blazor -- --seed from-md   [--dry-run]
// dotnet run --project src/IdeaCms.Blazor -- --seed repos [--org mindattic] [--local-root <dir>] [--dry-run]
var seedIdx = Array.IndexOf(args, "--seed");
if (seedIdx >= 0)
{
    var seedTarget = seedIdx + 1 < args.Length ? args[seedIdx + 1] : null;
    using var cliScope = app.Services.CreateScope();
    int exitCode;
    if (seedTarget == "core")
    {
        await cliScope.ServiceProvider.GetRequiredService<SeedService>().SeedAsync();
        Console.WriteLine("[seed] core seed completed.");
        exitCode = 0;
    }
    else if (seedTarget == "from-html")
        exitCode = await SeedFromHtmlCli.RunAsync(args, app.Services);
    else if (seedTarget == "from-md")
        exitCode = await SeedReadmesCli.RunAsync(args, app.Services);
    else if (seedTarget == "repos")
        exitCode = await SeedReposCli.RunAsync(args, app.Services);
    else
    {
        Console.Error.WriteLine($"[seed] Unknown target: '{seedTarget}'. Use 'core', 'from-html', 'from-md', or 'repos'.");
        exitCode = 1;
    }
    Environment.Exit(exitCode);
}

// ---- Server mode: listen first, then initialise. -------------------------------------------
// The server starts before the boot sequence, so App Service's warm-up and /_health see a live process
// within seconds instead of after every package scan and SQL round trip. A transient failure (an Entra
// SQL login reset while a cold container fights the other site for the B1 core) is retried; a permanent
// one stops the host with exit code 1 instead of escaping top-level statements as an unhandled exception,
// which the runtime turns into SIGABRT (exit 134) plus a core dump (M.IDEA-§4.14).
await app.StartAsync();
var stopping = app.Lifetime.ApplicationStopping;
try
{
    await IdeaCms.Blazor.StartupGate.RunWithRetryAsync(InitializeAsync,
        IdeaCms.Blazor.StartupRetryPolicy.Default, Console.Error, ct: stopping);
    readiness.MarkReady();
    Console.WriteLine("[startup] initialisation complete; serving.");
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested)
{
    // Shut down while still initialising (a restart or a stop): nothing to report.
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[startup] initialisation FAILED; stopping with exit code 1.{Environment.NewLine}{ex}");
    Environment.ExitCode = 1;
    await app.StopAsync();
    return;
}
await app.WaitForShutdownAsync();
