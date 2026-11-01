using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using CmsPage = IdeaCms.Core.Entities.Page;

namespace IdeaCms.Blazor.Cli;

/// <summary>
/// CLI mode: --seed-readmes
/// For every MindAttic project, upserts a Page record (under a "projects" parent) and a
/// ComponentMetadata record that points the FromMd component at its local README.md.
/// Usage: dotnet run --project src/IdeaCms.Blazor -- --seed-readmes [--dry-run]
/// </summary>
public static class SeedReadmesCli
{
    /// <summary>A project page; <paramref name="RelativePath"/> is under the MindAttic workspace root.</summary>
    public sealed record ProjectDef(string Slug, string Title, string RelativePath)
    {
        public string ReadmePath => Path.Combine(WorkspaceRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// The folder that holds every MindAttic repo: <c>MINDATTIC_WORKSPACE</c> when set, otherwise the parent
    /// of the IdeaCms checkout (found by walking up to <c>IdeaCms.slnx</c> from the working
    /// directory, then from the binaries).
    /// </summary>
    public static string WorkspaceRoot { get; } = ResolveWorkspaceRoot();

    public static string ResolveWorkspaceRoot()
    {
        if (Environment.GetEnvironmentVariable("MINDATTIC_WORKSPACE") is { Length: > 0 } configured)
            return Path.GetFullPath(configured);
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "IdeaCms.slnx")))
                    return dir.Parent?.FullName ?? dir.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    public static readonly ProjectDef[] Projects =
    [
        // ---- Original 9 ----
        new("ideas",            "IdeaCms",        "IdeaCms/README.md"),
        new("idiotproof",       "IdiotProof",             "IdiotProof/README.md"),
        new("vault",            "MindAttic Vault",        "MindAttic.Vault/README.md"),
        new("legion",           "MindAttic Legion",       "MindAttic.Legion/README.md"),
        new("thinktank",        "ThinkTank",              "ThinkTank/README.md"),
        new("tutor",            "Tutor",                  "Tutor/README.md"),
        new("taxrate",          "TaxRateCollector",       "TaxRateCollector/README.md"),
        new("psst",             "MindAttic Psst",         "MindAttic.Psst/README.md"),
        new("prose",    "Prose",          "Prose/README.md"),
        // ---- New projects ----
        new("bugoutbag",        "BugOutBag",              "BugOutBag/Readme.md"),
        new("chimesh",          "ChiMesh",                "ChiMesh/Readme.md"),
        new("claudia",          "Claudia",                "Claudia/Readme.md"),
        new("cursory",          "Cursory",                "Cursory/Readme.md"),
        new("fractionsofacent", "FractionsOfACent",       "FractionsOfACent/Readme.md"),
        new("gridgame2026",     "GridGame 2026",          "GridGame2026/Readme.md"),
        new("hyperspace",       "Hyperspace",             "Hyperspace/Readme.md"),
        new("mediabutler",      "MediaButler",            "MediaButler/Readme.md"),
        new("authentication",   "MindAttic Authentication", "MindAttic.Authentication/Readme.md"),
        new("mindattic-com",    "mindattic.com",          "mindattic.com/Readme.md"),
        new("deploy",           "MindAttic Deploy",       "MindAttic.Deploy/Readme.md"),
        new("helpers",          "MindAttic Helpers",      "MindAttic.Helpers/Readme.md"),
        new("launcher",         "MindAttic Launcher",     "MindAttic.Launcher/Readme.md"),
        new("mobile",           "MindAttic Mobile",       "MindAttic.Mobile/Readme.md"),
        new("uiux",             "MindAttic UiUx",         "MindAttic.Components/README.md"),
        new("mindatticcares-com", "mindatticcares.com",   "mindatticcares.com/Readme.md"),
        new("ryandebraal-com",  "ryandebraal.com",        "ryandebraal.com/Readme.md"),
        new("skindeep",         "SkinDeep",               "SkinDeep/Readme.md"),
    ];

    static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        var dryRun = args.Contains("--dry-run");
        if (dryRun) Console.WriteLine("[seed-readmes] DRY RUN — no DB writes.");

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        var site = await db.Sites.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (site is null) { Console.Error.WriteLine("[seed-readmes] No site found. Create a site first."); return 1; }
        var siteId = site.Id;
        Console.WriteLine($"[seed-readmes] Using site: {site.Key} (Id={siteId})");

        // Ensure the "projects" parent page exists.
        var parentPage = await db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.SiteId == siteId && p.Slug == "projects");
        if (parentPage is null)
        {
            if (!dryRun)
            {
                var now = DateTime.UtcNow;
                parentPage = new CmsPage
                {
                    SiteId = siteId, Slug = "projects", Title = "Projects",
                    Kind = PageKind.Data,
                    BodyHtml = "<h1>MindAttic Projects</h1>",
                    BodyTrust = ContentTrust.Author,
                    IsPublished = true, Enabled = true,
                    CreatedUtc = now, ModifiedUtc = now,
                };
                db.Pages.Add(parentPage);
                await db.SaveChangesAsync();
                Console.WriteLine("[seed-readmes] Created parent page: /projects");
            }
            else Console.WriteLine("[seed-readmes] [DRY] Would create parent page: /projects");
        }
        else Console.WriteLine("[seed-readmes] Parent page exists: /projects");

        foreach (var proj in Projects)
        {
            var slug = $"projects/{proj.Slug}";
            var readmeExists = File.Exists(proj.ReadmePath);
            var markdown = readmeExists ? await File.ReadAllTextAsync(proj.ReadmePath) : "";

            if (!readmeExists)
                Console.WriteLine($"[seed-readmes] WARN: README not found at {proj.ReadmePath}");

            var existingPage = await db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.SiteId == siteId && p.Slug == slug);
            Guid pageUid;

            if (existingPage is null)
            {
                if (!dryRun)
                {
                    var now = DateTime.UtcNow;
                    var newPage = new CmsPage
                    {
                        SiteId = siteId, ParentId = parentPage?.Id,
                        Slug = slug, Title = proj.Title,
                        Kind = PageKind.Data,
                        BodyHtml = "<Component.Frommd />",
                        BodyTrust = ContentTrust.Author,
                        IsPublished = true, Enabled = true,
                        CreatedUtc = now, ModifiedUtc = now,
                    };
                    db.Pages.Add(newPage);
                    await db.SaveChangesAsync();
                    pageUid = newPage.Uid;
                    Console.WriteLine($"[seed-readmes] Created page: /{slug} ({proj.Title})");
                }
                else
                {
                    Console.WriteLine($"[seed-readmes] [DRY] Would create page: /{slug} ({proj.Title})");
                    continue;
                }
            }
            else
            {
                pageUid = existingPage.Uid;
                Console.WriteLine($"[seed-readmes] Page exists: /{slug}");
            }

            // Upsert ComponentMetadata for the frommd slot.
            var meta = await db.ComponentMetadata
                .FirstOrDefaultAsync(m => m.PageUid == pageUid && m.ComponentKey == "frommd" && m.SlotName == "main");

            var metadataJson = JsonSerializer.Serialize(new
            {
                localSourceFile = proj.ReadmePath,
                markdown,
                lastSynced = DateTime.UtcNow,
            }, JsonOpts);

            if (meta is null)
            {
                if (!dryRun)
                {
                    var now = DateTime.UtcNow;
                    db.ComponentMetadata.Add(new ComponentMetadata
                    {
                        PageUid = pageUid, ComponentKey = "frommd", SlotName = "main",
                        MetadataJson = metadataJson, CreatedUtc = now, ModifiedUtc = now,
                    });
                    await db.SaveChangesAsync();
                    Console.WriteLine($"[seed-readmes]   + ComponentMetadata: {proj.ReadmePath}");
                }
                else Console.WriteLine($"[seed-readmes] [DRY]   Would create ComponentMetadata: {proj.ReadmePath}");
            }
            else
            {
                if (!dryRun)
                {
                    meta.MetadataJson = metadataJson;
                    meta.ModifiedUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    Console.WriteLine($"[seed-readmes]   ~ Updated ComponentMetadata: {proj.ReadmePath}");
                }
                else Console.WriteLine($"[seed-readmes] [DRY]   Would update ComponentMetadata: {proj.ReadmePath}");
            }
        }

        Console.WriteLine("[seed-readmes] Done.");
        return 0;
    }
}
