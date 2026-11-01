using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Discovery;
using IdeaCms.Core.Portability;
using IdeaCms.Core.Rendering;
using IdeaCms.Core.Secrets;
using IdeaCms.Core.Services;
using IdeaCms.Core.Sites;
using MindAttic.Log.Extensions;
using MindAttic.Media;
using MindAttic.Vault.Paths;

namespace IdeaCms.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the CMS Core: DbContext factory, the compiled discovery source over the given citizen
    /// assemblies, the catalog + type resolver, the raw-content gate, and auth/seed/discovery services.
    /// </summary>
    public static IServiceCollection AddIdeaCms(
        this IServiceCollection services, string connectionString, params Assembly[] citizenAssemblies)
    {
        // Phase 2 (MindAttic.Log 3.0.0, see its docs/BIBLE.md §1.1): logs now live in their own
        // standalone MindAttic.Log.db under Vault's shared roaming root, not CmsDbContext's SQL
        // Server database — Phase 1 wrote dbo.MindAttic_Log into the same database CmsDbContext
        // uses; that's superseded. The one-time copy of Phase 1's historical rows (and dropping
        // that table) happens at startup — see Program.cs.
        services.AddMindAtticLog(o =>
        {
            o.Application = "IdeaCms";
            o.DbPath = VaultPaths.LoggingDbPath("IdeaCms");
        });

        services.AddDbContextFactory<CmsDbContext>(o => o.UseSqlServer(connectionString));
        // The MindAttic.Authentication seam resolves AddScoped<IAuthDataContext>(sp => GetRequiredService<CmsDbContext>()),
        // and AuthBootstrapper/IUserStore are scoped — so CmsDbContext needs a SCOPED registration too (the
        // factory alone doesn't provide one). Both share the same connection string. optionsLifetime:Singleton
        // is REQUIRED here: the singleton factory builds its options from the same IDbContextOptionsConfiguration
        // set, so leaving it scoped (the AddDbContext default) makes the factory resolve a scoped service from
        // the root provider -> the app fails DI validation on startup.
        services.AddDbContext<CmsDbContext>(o => o.UseSqlServer(connectionString),
            optionsLifetime: ServiceLifetime.Singleton);

        // Discovery + catalog (singletons: one shared catalog snapshot for the app). The ALC-aware resolver
        // loads PACKAGE citizens through a per-package collectible context and delegates every other
        // descriptor to the default resolver — so compiled content is unchanged and package types resolve
        // once their bytes are extracted (otherwise a placeholder).
        services.AddSingleton<DefaultTypeResolver>();
        services.AddSingleton<ITypeResolver>(sp => new AlcAwareTypeResolver(
            sp.GetRequiredService<DefaultTypeResolver>(), sp.GetRequiredService<IPackageExtractor>()));
        services.AddSingleton<ContentCatalog>();
        services.AddSingleton<IContentCatalog>(sp => sp.GetRequiredService<ContentCatalog>());
        var assemblies = citizenAssemblies.Length > 0 ? citizenAssemblies : new[] { Assembly.GetEntryAssembly()! };
        services.AddSingleton<ICmsContentSource>(_ => new CompiledContentSource(assemblies));
        services.AddSingleton<DiscoveryService>();

        // Rendering.
        services.AddSingleton<IRawContentGate, RawContentGate>();
        // The CmsInclude SDK primitive resolves this feature via IRenderContext.TryGetFeature, so a COMPILED
        // page can drop a Component/Control by string id at runtime (the compiled-page analog of a data
        // page's <IdeaCms.…/> include tag). Singleton: depends only on the catalog + alert sink.
        services.AddSingleton<IIncludeRenderer, IncludeRenderer>();
        // The TableOfContents widget (and any nav citizen) resolves this via TryGetFeature to list the
        // current page's children at render — the page hierarchy (Page.ParentId/SortOrder) surfaced to
        // package citizens with no compile-time host reference. Depends only on the DbContext factory.
        services.AddSingleton<IPageTree, PageTreeFeature>();

        // Seed (CMS content). Auth seeding is the library's AuthBootstrapper, wired in the Web host.
        services.AddScoped<SeedService>();

        // Phase-2: admin inbox + lifecycle + page authoring + the render-alert sink.
        services.AddScoped<IAdminInboxService, AdminInboxService>();
        services.AddSingleton<IRenderAlertSink, RenderAlertSink>();
        services.AddScoped<IContentLifecycleService, ContentLifecycleService>();
        services.AddScoped<IPageAdminService, PageAdminService>();
        services.AddScoped<IPageHistoryService, PageHistoryService>();
        services.AddScoped<ICmsRoleService, CmsRoleService>();
        services.AddScoped<IWorkflowService, WorkflowService>();
        services.AddScoped<IWidgetInstanceSettingsService, WidgetInstanceSettingsService>();
        services.AddScoped<IInstanceConfigService, InstanceConfigService>();
        services.AddSingleton<IPageValidator, PageValidator>();
        services.AddScoped<IComponentMetadataStore, ComponentMetadataService>();
        services.AddScoped<ISlugRedirectService, SlugRedirectService>();

        // One deployment, many domains: Site.HostBindings has been in the schema since migration #1;
        // the resolver is what finally reads it. Stateless, so a singleton — it takes the DbContext
        // per call rather than holding one.
        services.AddSingleton<ISiteResolver, SiteResolver>();
        services.AddScoped<ISiteAdminService, SiteAdminService>();

        // Phase-5: .idea package install (validate + persist bytes + extract + register rows + ALC resolve).
        // Local file store/extractor by default; the ADR's Azure Blob backing slots in behind IPackageBlobStore.
        // IdeaCms:Packages:StorageRoot moves both off the per-user default (%APPDATA%\MindAttic\IdeaCms), so two
        // instances on one machine never extract over each other's loaded assemblies.
        services.AddSingleton<IPackageBlobStore>(sp => new LocalFilePackageBlobStore(PackageStorageDir(sp, "packages")));
        services.AddSingleton<IPackageExtractor>(sp => new PackageExtractor(PackageStorageDir(sp, "extracted")));
        // The one trusted public certificate every install verifies a package's content signature
        // against (MAI: NuGet distribution + signing) — Vault-backed, cached after first read.
        services.AddSingleton<IPackageSigningTrust, VaultPackageSigningTrust>();
        services.AddScoped<IPackageInstallService, PackageInstallService>();
        services.AddScoped<IPackageRegistryService, PackageRegistryService>();

        // Catalog browsing (admin "Catalog" tab): live GitHub REST enumeration of what's published on
        // the feed, reusing the same two config keys IdeaListResolverFactory already reads — no new
        // config surface. Registered even when the feed isn't configured; IPackageCatalogFetcher.IsConfigured
        // just comes back false and ListAvailableAsync short-circuits without ever calling the API.
        services.AddSingleton<IPackageCatalogFetcher>(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var org = ExtractGitHubOrg(configuration["IdeaCms:NuGetFeedUrl"]);
            var pat = configuration["MindAttic:Vault:Tokens:github-packages-pat"];
            return new GitHubPackagesCatalogFetcher(org, pat);
        });
        services.AddScoped<IPackageCatalogService, PackageCatalogService>();

        // Media asset storage. Caller configures MediaStoreOptions.MediaRoot (done in Program.cs).
        services.AddMedia<CmsDbContext>();

        return services;
    }

    /// <summary>The feed's org from its NuGet v3 index URL, e.g.
    /// <c>https://nuget.pkg.github.com/MindAttic/index.json</c> -&gt; <c>"MindAttic"</c>. Null when the
    /// feed isn't configured or the URL doesn't have the expected shape.</summary>
    private static string? ExtractGitHubOrg(string? feedUrl)
    {
        if (string.IsNullOrWhiteSpace(feedUrl)) return null;
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri)) return null;
        var segment = uri.Segments.FirstOrDefault(s => s.Trim('/').Length > 0);
        return segment?.Trim('/');
    }

    /// <summary>Null keeps the store's own default (%APPDATA%\MindAttic\IdeaCms\{leaf}).</summary>
    private static string? PackageStorageDir(IServiceProvider sp, string leaf) =>
        sp.GetService<IConfiguration>()?["IdeaCms:Packages:StorageRoot"] is { Length: > 0 } root ? Path.Combine(root, leaf) : null;
}
