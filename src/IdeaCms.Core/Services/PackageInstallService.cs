using Microsoft.EntityFrameworkCore;
using IdeaCms.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Discovery;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Rendering;
using IdeaCms.Packaging;

namespace IdeaCms.Core.Services;

/// <summary>A package could not be installed (malformed, invalid, blocked, or a downgrade).</summary>
public class InstallException(string message) : Exception(message);

/// <summary>A package's content signature failed verification (missing, malformed, untrusted signer,
/// or the signature does not match the archive's content) — see <see cref="IdeaCms.Packaging.PackageSigner"/>.</summary>
public sealed class PackageSignatureException(string message) : InstallException(message);

public interface IPackageInstallService
{
    /// <summary>
    /// Validate a <c>.idea</c> stream and, if the plan permits, register it: upsert the
    /// <see cref="InstalledPackage"/> registry row and a mirrored <see cref="CmsContentDefinition"/>
    /// (Origin=Package), then reload the live catalog. Does NOT load the package assembly — that is the
    /// deferred Phase-5/B ALC loader; until then the descriptor's CLR type stays unresolved and the
    /// citizen degrades to a render placeholder rather than crashing. Throws <see cref="InstallException"/>
    /// for an invalid/blocked/downgrade package.
    /// </summary>
    Task<InstallPlan> InstallAsync(Stream ideaBytes, bool allowOverride, CancellationToken ct = default);

    /// <summary>Soft-disable an installed version: flip Enabled=false on both rows and reload. Bytes/rows remain.</summary>
    Task DisableAsync(string category, string key, int version, CancellationToken ct = default);
}

/// <summary>
/// The host-side install tail. All format/version/collision logic is the pure
/// <see cref="IdeaCms.Packaging"/> library; this only does the IO (read bytes, hash, persist rows)
/// and the live-catalog reload. Soft by construction — nothing is ever hard-removed.
/// </summary>
public sealed class PackageInstallService(
    IDbContextFactory<CmsDbContext> dbFactory,
    DiscoveryService discovery,
    IPackageBlobStore blobStore,
    IPackageExtractor extractor,
    IRenderAlertSink alerts,
    IPackageSigningTrust signingTrust,
    IAdminInboxService adminInbox,
    Microsoft.Extensions.Configuration.IConfiguration? configuration = null) : IPackageInstallService
{
    // Until the system is "locked down", a changed package at an already-installed version overwrites
    // it instead of being refused. Flip IdeaCms:Packages:OverwriteVersionConflicts to false to lock down.
    private bool OverwriteVersionConflicts =>
        bool.TryParse(configuration?["IdeaCms:Packages:OverwriteVersionConflicts"], out var on) && on;

    public async Task<InstallPlan> InstallAsync(Stream ideaBytes, bool allowOverride, CancellationToken ct = default)
    {
        // Buffer once: we need the bytes for both the (seekable) archive read and blob storage.
        using var buffer = new MemoryStream();
        await ideaBytes.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        using var archive = IdeaArchiveReader.Open(new MemoryStream(bytes));

        // Signature verification comes BEFORE the manifest is even trusted for parsing — every install
        // path (boot library scan, .idealist import, --install, admin upload, NuGet fetch) funnels
        // through here, so this is the one place a tampered/unsigned .idea is rejected uniformly.
        var verify = PackageSigner.Verify(archive, signingTrust.TrustedCertificate);
        if (verify != PackageSigner.VerifyOutcome.Ok)
            throw new PackageSignatureException($"package signature check failed: {verify}.");

        // Content identity, NOT a whole-file hash: RSASSA-PSS signatures are randomized, so re-signing
        // byte-identical content produces different whole-file bytes on every sign. Hashing the same
        // canonical manifest the signature itself covers means "was this package's actual content
        // re-published unchanged" stays stable across re-signs, while a genuine content change still
        // changes the hash — exactly what hash-conflict detection (PackageVersionResolver.Plan) needs.
        var sha = Sha256Hasher.OfBytes(PackageSigner.CanonicalManifest(archive));

        if (!archive.TryReadManifest(out var manifest, out var error) || manifest is null)
            throw new InstallException(error ?? "package manifest could not be read.");
        var rawJson = archive.ReadManifestJson()!;   // non-null: TryReadManifest succeeded
        var binEntries = archive.BinEntries();

        var validation = ManifestValidator.Validate(manifest, binEntries);
        if (!validation.IsValid)
            throw new InstallException("invalid package — " + validation.Summary);

        if (!Enum.TryParse<ContentKind>(manifest.Category, ignoreCase: true, out var kind))
            throw new InstallException($"category '{manifest.Category}' is not a known ContentKind; cannot register the catalog row.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var installedRows = await db.InstalledPackages
            .Where(p => p.Key == manifest.Key && p.Category == manifest.Category)
            .ToListAsync(ct);
        var installedRefs = installedRows
            .Select(p => new InstalledRef(p.Category, p.Key, p.Version, p.Enabled, p.IsActiveVersion, p.Sha256))
            .ToList();

        var compiledKeyExists = await db.ContentDefinitions.AnyAsync(
            c => c.Origin == ContentOrigin.Compiled && c.Kind == kind && c.Key == manifest.Key && c.IsActive, ct);

        var plan = PackageVersionResolver.Plan(manifest, sha, installedRefs, compiledKeyExists, allowOverride,
            overwriteHashConflicts: OverwriteVersionConflicts);
        switch (plan.Action)
        {
            case InstallAction.NoOpAlreadyInstalled:
                return plan;
            case InstallAction.HashConflict:
                await adminInbox.RaiseAsync("Error", "PackageInstall",
                    subject: $"Install conflict: {manifest.Category}/{manifest.Key} v{manifest.Version}",
                    body: plan.Reason ?? "sha256 mismatch against the already-installed version.",
                    dedupKey: AdminInboxService.DedupKey("package-hash-conflict", manifest.Category, manifest.Key, manifest.Version.ToString()),
                    ct);
                throw new InstallException(plan.Reason ?? "install was rejected: content hash conflict.");
            case InstallAction.Blocked:
            case InstallAction.RejectDowngrade:
                throw new InstallException(plan.Reason ?? "install was rejected.");
        }

        var now = DateTime.UtcNow;

        // ---- Declared-REQUIRED dependencies (BLOCKING): every requires[] entry must already be installed
        // and enabled. Unlike uses[], a missing required dependency is a hard reject — the author explicitly
        // declared a structural prerequisite, not just a runtime reference. ----
        foreach (var u in manifest.Requires ?? [])
            if (!IncludeReferenceParser.TryParseUse(u, out _, out _, out _))
                throw new InstallException(
                    $"REQUIRES_UNPARSEABLE: requires[] entry '{u}' is not a valid dependency reference " +
                    $"(expected 'Kind.key' or 'Kind.key@version'). Update the package manifest.");
        foreach (var (depKind, depKey, depVer) in IncludeReferenceParser.ParseUses(manifest.Requires))
        {
            var present = await db.ContentDefinitions.AnyAsync(
                c => c.Kind == depKind && c.Key == depKey && c.Enabled && c.IsActive
                     && (depVer == null || c.Version == depVer), ct);
            if (!present)
                throw new InstallException(
                    $"REQUIRES_UNMET: required dependency '{depKey}' (kind '{depKind}'{(depVer.HasValue ? $" v{depVer}" : "")}) " +
                    $"is not installed or not enabled. Install it first before installing '{manifest.Key}'.");
        }

        // Persist the verbatim bytes (content-addressed path, safe to save before the DB row).
        var blobPath = await blobStore.SaveAsync(manifest.Category, manifest.Key, manifest.Version, bytes, ct);

        // ---- Registry row (idempotent upsert by the unique (Category,Key,Version)). ----
        var pkg = installedRows.FirstOrDefault(p => p.Version == manifest.Version)
                  ?? AddNew(db, new InstalledPackage());
        pkg.Category = manifest.Category;
        pkg.Kind = manifest.Kind;
        pkg.Key = manifest.Key;
        pkg.Version = manifest.Version;
        pkg.DisplayName = manifest.DisplayName;
        pkg.ManifestJson = rawJson;                       // verbatim — preserves any forward-compat fields
        pkg.ManifestVersion = manifest.ManifestVersion;
        pkg.Sha256 = sha;
        pkg.BlobPath = blobPath;                          // points at the persisted .idea bytes
        pkg.Enabled = true;
        pkg.IsActiveVersion = plan.MakeActiveVersion;
        pkg.InstalledUtc = now;

        // Retain prior active versions; just flip them inactive.
        foreach (var (_, _, ver) in plan.DeactivatePriorVersions)
        {
            var prior = installedRows.FirstOrDefault(p => p.Version == ver);
            if (prior is not null) prior.IsActiveVersion = false;
        }

        // ---- Mirrored catalog row (Origin=Package) so the citizen is registered without the ALC loader. ----
        var def = await db.ContentDefinitions.FirstOrDefaultAsync(
            c => c.Origin == ContentOrigin.Package && c.Kind == kind && c.Key == manifest.Key && c.Version == manifest.Version, ct)
            ?? AddNew(db, new CmsContentDefinition());
        def.Kind = kind;
        def.Key = manifest.Key;
        def.Version = manifest.Version;
        def.Origin = ContentOrigin.Package;
        def.DisplayName = manifest.DisplayName;
        def.Category = manifest.Category;
        def.Strategy = RenderStrategy.ClrType;
        def.RenderMode = ParseRenderMode(manifest.RenderMode);
        def.Scope = ParseScope(manifest.Scope);
        def.ClrTypeName = manifest.EntryType;             // resolves only once the ALC loader (B) lands
        def.AssemblyName = manifest.AssemblyName;
        def.AssetMount = $"/_ideas/{manifest.Category}/{manifest.Key}/{manifest.Version}";   // served by the /_ideas route
        def.Priority = 50;                                // Package < Compiled(100)
        def.IsActive = true;
        def.Enabled = true;
        def.AllowOverride = allowOverride;
        def.DiscoveredUtc = now;

        try { await db.SaveChangesAsync(ct); }   // pkg.Id is now generated, so the seed can stamp SourcePackageId.
        catch (DbUpdateException)
        {
            // Concurrent install of the same (Category,Key,Version) raced past the NoOp check above.
            return new InstallPlan(InstallAction.NoOpAlreadyInstalled,
                $"{manifest.Category}/{manifest.Key} v{manifest.Version} was installed by a concurrent request.",
                MakeActiveVersion: false, []);
        }

        // Extract bin/ only after the DB row is committed so a concurrent-install race that returns
        // NoOpAlreadyInstalled above never leaves orphaned bin/ directories on disk.
        if (string.Equals(manifest.Kind, "code", StringComparison.Ordinal))
            extractor.Extract(archive, manifest.Category, manifest.Key, manifest.Version);

        // ---- Seed-on-install: a Page (code) package may carry data/page.json to make itself routable on
        // upload (idempotent by (SiteId, Slug); never clobbers a row another package or an admin owns). ----
        if (kind == ContentKind.Page && string.Equals(manifest.Kind, "code", StringComparison.Ordinal)
            && archive.ReadPageSeed() is { Slug.Length: > 0 } seed)
        {
            await ApplyPageSeedAsync(db, pkg, manifest, seed, now, ct);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { /* idempotent — slug already occupied by another package or admin page */ }
        }

        // ---- Declared-dependency advisory (NON-blocking): a uses[] id with no installed+enabled definition.
        // String-id references are late-bound (the theme/component may be installed afterward), so this is a
        // warning to the operator (Admin Inbox), never a hard reject. ----
        foreach (var (depKind, depKey, depVer) in IncludeReferenceParser.ParseUses(manifest.Uses))
        {
            var present = await db.ContentDefinitions.AnyAsync(
                c => c.Kind == depKind && c.Key == depKey && c.Enabled && c.IsActive
                     && (depVer == null || c.Version == depVer), ct);
            if (!present)
                try { alerts.RaiseMissing(depKind, depKey, depVer, Guid.Empty, $"install:{manifest.Key}@{manifest.Version}"); }
                catch { /* an advisory never breaks an install */ }
        }

        await discovery.ReloadCatalogAsync(ct);
        return plan;
    }

    /// <summary>
    /// Idempotent Page upsert by (SiteId, Slug), stamping <see cref="Page.SourcePackageId"/> for provenance.
    /// A compiled package page carries NO author raw markup, so it is stamped <see cref="ContentTrust.Untrusted"/>
    /// (the trust gate governs only the inline-HTML path a code page never enters). Only PACKAGE-owned fields
    /// are updated on re-install/upgrade (re-pointing <see cref="Page.ComponentTypeName"/> V1→V2 is the upgrade);
    /// admin-owned fields (Enabled, Slug, SortOrder, IsPublished) are preserved, and a row owned by a DIFFERENT
    /// package is never clobbered.
    /// </summary>
    private static async Task ApplyPageSeedAsync(
        CmsDbContext db, InstalledPackage pkg, IdeaManifest manifest, PageSeed seed, DateTime now, CancellationToken ct)
    {
        var site = seed.SiteKey is { Length: > 0 } sk
            ? await db.Sites.FirstOrDefaultAsync(s => s.Key == sk, ct)
            : await db.Sites.FirstOrDefaultAsync(s => s.IsDefault, ct) ?? await db.Sites.FirstOrDefaultAsync(ct);
        if (site is null) return;   // nothing to attach to yet — skip rather than invent a site
        var siteId = site.Id;
        var slug = seed.Slug.Trim('/').ToLowerInvariant();

        // IgnoreQueryFilters: must see soft-deleted rows to avoid a duplicate-slug INSERT on re-install.
        var existing = await db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.SiteId == siteId && p.Slug == slug, ct);

        if (existing is null)
        {
            db.Pages.Add(new Page
            {
                SiteId = siteId, Slug = slug,
                Title = string.IsNullOrWhiteSpace(seed.Title) ? manifest.DisplayName : seed.Title,
                ThemeKey = seed.ThemeKey, ThemeVersion = seed.ThemeVersion,
                Kind = PageKind.Code,
                ComponentTypeName = manifest.EntryType,
                AssemblyName = manifest.AssemblyName,
                BodyHtml = null, BodyTrust = ContentTrust.Untrusted, AuthoredByUserId = "system-package",
                IsPublished = seed.Published, Enabled = true,
                SourcePackageId = pkg.Id,
                CreatedUtc = now, ModifiedUtc = now,
            });
            return;
        }

        // Does THIS logical package own the row? Ownership is by (Category, Key), NOT the version row id —
        // each version is a distinct InstalledPackage row, so a V1→V2 upgrade must still be recognized as
        // the same owner. An unowned (admin-created) row is adopted on first install.
        var owned = existing.SourcePackageId is null || existing.SourcePackageId == pkg.Id;
        if (!owned && existing.SourcePackageId is int ownerId)
        {
            var owner = await db.InstalledPackages.FirstOrDefaultAsync(p => p.Id == ownerId, ct);
            owned = owner is null   // prior owner row deleted → adopt
                || (string.Equals(owner.Category, manifest.Category, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(owner.Key, manifest.Key, StringComparison.Ordinal));
        }

        // Update only package-owned fields; admin-owned (Enabled, Slug, SortOrder, IsPublished) are preserved.
        // Ownership is confirmed before undeleting to avoid reinstating a page that a different package owns.
        if (owned)
        {
            if (existing.IsDeleted)
            {
                existing.IsDeleted = false;
                existing.DeletedUtc = null;
            }
            existing.Kind = PageKind.Code;
            existing.ComponentTypeName = manifest.EntryType;     // V1 → V2 re-point = the upgrade path
            existing.AssemblyName = manifest.AssemblyName;
            existing.ThemeKey = seed.ThemeKey ?? existing.ThemeKey;
            existing.ThemeVersion = seed.ThemeVersion ?? existing.ThemeVersion;
            existing.SourcePackageId = pkg.Id;
            existing.ModifiedUtc = now;
        }
        // else: a DIFFERENT package (or an admin) owns this slug — leave it untouched (no clobber).
    }

    public async Task DisableAsync(string category, string key, int version, CancellationToken ct = default)
    {
        if (!Enum.TryParse<ContentKind>(category, ignoreCase: true, out var kind))
            throw new InstallException($"category '{category}' is not a known ContentKind.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var pkg = await db.InstalledPackages.FirstOrDefaultAsync(
            p => p.Category == category && p.Key == key && p.Version == version, ct);
        if (pkg is not null) pkg.Enabled = false;

        var def = await db.ContentDefinitions.FirstOrDefaultAsync(
            c => c.Origin == ContentOrigin.Package && c.Kind == kind && c.Key == key && c.Version == version, ct);
        if (def is not null) def.Enabled = false;

        if (pkg is null && def is null) return;
        await db.SaveChangesAsync(ct);
        await discovery.ReloadCatalogAsync(ct);
    }

    private static T AddNew<T>(CmsDbContext db, T entity) where T : class
    {
        db.Add(entity);
        return entity;
    }

    private static CmsRenderMode ParseRenderMode(string s) =>
        string.Equals(s, "Static", StringComparison.OrdinalIgnoreCase) ? CmsRenderMode.Static : CmsRenderMode.InteractiveServer;

    private static PlacementScope ParseScope(string s) =>
        string.Equals(s, "Global", StringComparison.OrdinalIgnoreCase) ? PlacementScope.Global : PlacementScope.Placeable;
}
