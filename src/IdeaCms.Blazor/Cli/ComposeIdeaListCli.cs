using Microsoft.EntityFrameworkCore;
using IdeaCms.Core.Data;
using MindAttic.Export.Artifacts;
using IdeaCms.Core.Portability;
using IdeaCms.Core.Rendering;
using MindAttic.Media;

namespace IdeaCms.Blazor.Cli;

/// <summary>
/// CLI mode: <c>--compose-idealist &lt;output.idealist&gt;</c>. Builds an idealist from explicit
/// <c>--package</c> references and, optionally, an existing site's content — the path to a
/// provisioning-only idealist (packages, no site content) without hand-editing <c>idealist.json</c>.
/// <para>
/// With no <c>--from-site</c>, the result is packages-only: <c>Site = null</c>, <c>Pages = []</c>.
/// With <c>--from-site</c>, it behaves like <c>--export-idealist</c> for that site's content, unioning
/// the named <c>--package</c> refs with whatever that export auto-discovers.
/// </para>
/// Usage: <c>dotnet run --project src/IdeaCms.Blazor -- --compose-idealist provision.idealist
/// --package Theme.cyberspace@1 --package Plugin.tooltip@2 [--from-site rdb] [--slug projects/]
/// [--no-media] [--dry-run]</c>
/// </summary>
public static class ComposeIdeaListCli
{
    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        var target = ExportIdeaListCli.ArgValue(args, "--compose-idealist");
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("[compose-idealist] No output file given. Usage: --compose-idealist <output.idealist> [--package Kind.key@version ...] [--from-site key] [--slug prefix] [--no-media] [--dry-run]");
            return 1;
        }
        var outPath = Path.GetFullPath(target);

        var explicitPackages = ExportIdeaListCli.ArgValues(args, "--package");
        foreach (var p in explicitPackages)
        {
            if (!IncludeReferenceParser.TryParseUse(p, out _, out _, out var version) || version is null)
            {
                Console.Error.WriteLine($"[compose-idealist] --package '{p}' is not a valid pinned reference (expected 'Kind.key@version').");
                return 1;
            }
        }

        var dryRun = args.Contains("--dry-run");
        var includeMedia = !args.Contains("--no-media");
        var slugPrefix = ExportIdeaListCli.ArgValue(args, "--slug");
        var fromSiteKey = ExportIdeaListCli.ArgValue(args, "--from-site")?.Trim().ToLowerInvariant();

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        int? siteId = null;
        if (fromSiteKey is { Length: > 0 })
        {
            var site = await db.Sites.FirstOrDefaultAsync(s => s.Key == fromSiteKey);
            if (site is null)
            {
                Console.Error.WriteLine($"[compose-idealist] No site with key \"{fromSiteKey}\". "
                                      + $"Known keys: {string.Join(", ", await db.Sites.Select(s => s.Key).ToListAsync())}");
                return 1;
            }
            siteId = site.Id;
        }

        var exporter = new IdeaListExporter(db, scope.ServiceProvider.GetRequiredService<IMediaStore>());
        var options = new IdeaListExportOptions
        {
            IncludeSiteContent = fromSiteKey is { Length: > 0 },
            SlugPrefix = slugPrefix,
            IncludeMedia = includeMedia,
            ExtraPackages = explicitPackages,
        };

        IdeaListExportResult result;
        if (dryRun)
        {
            result = await exporter.ExportAsync(null, siteId, options, ExportIdeaListCli.MakeLog("compose-idealist"));
            Console.WriteLine($"[compose-idealist] DRY RUN — would write {outPath}: {result.List.Packages.Count} package(s), "
                             + $"{result.List.Pages.Count} page(s).");
            return 0;
        }

        // The .idealist bytes are exactly what the exporter writes into the zip (HOUSE-LAW-5);
        // ArtifactWriter only owns placing the file: folder created, atomic write, and — as before —
        // an existing file at the exact path the caller named is overwritten.
        IdeaListExportResult? written = null;
        await ArtifactWriter.WriteZipAsync(
            Path.GetDirectoryName(outPath)!, Path.GetFileName(outPath),
            async (zip, _) => written = await exporter.ExportAsync(zip, siteId, options, ExportIdeaListCli.MakeLog("compose-idealist")),
            new ArtifactOptions { Existing = ExistingArtifact.Overwrite, SanitizeName = false });
        result = written!;

        Console.WriteLine($"[compose-idealist] wrote {outPath} ({IdeaListExporter.Describe(new FileInfo(outPath).Length)}): "
                         + $"{result.List.Packages.Count} package(s), {result.List.Pages.Count} page(s).");
        return result.Ok ? 0 : 1;
    }
}
