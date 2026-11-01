using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using IdeaCms.Core.Data;
using MindAttic.Export.Artifacts;
using IdeaCms.Core.Portability;
using MindAttic.Media;

namespace IdeaCms.Blazor.Cli;

/// <summary>
/// CLI mode: <c>--export-idealist &lt;file&gt;</c>. Writes every authored page, its settings, its
/// per-component metadata and the media it references into one portable archive — plus a Packages[]
/// list auto-discovered from what those pages actually reference.
/// <para>
/// Argument parsing and console reporting only — the work is <see cref="IdeaListExporter"/> in Core.
/// See M.IDEA-§4.9.
/// </para>
/// Usage: <c>dotnet run --project src/IdeaCms.Blazor -- --export-idealist site.idealist
/// [--site rdb] [--slug projects/] [--no-media] [--dry-run]</c>
/// </summary>
public static class ExportIdeaListCli
{
    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        var dryRun = args.Contains("--dry-run");
        var includeMedia = !args.Contains("--no-media");
        var slugPrefix = ArgValue(args, "--slug");

        var target = ArgValue(args, "--export-idealist");
        if (string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("[export-idealist] No output file given. Usage: --export-idealist <file.idealist> [--site key] [--slug prefix] [--no-media] [--dry-run]");
            return 1;
        }
        // `dotnet run --project` runs from the PROJECT directory, so a relative path here is almost
        // never where the caller thinks it is. Resolve it and say where it landed.
        var outPath = Path.GetFullPath(target);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        // A deployment can host several domains (M.IDEA-§4.10), so an export names WHICH site it carries.
        // Default: the default site — the single-site behaviour, unchanged.
        var siteKey = ArgValue(args, "--site")?.Trim().ToLowerInvariant();
        var site = siteKey is { Length: > 0 }
            ? await db.Sites.FirstOrDefaultAsync(s => s.Key == siteKey)
            : await db.Sites.OrderBy(s => s.IsDefault ? 0 : 1).ThenBy(s => s.Id).FirstOrDefaultAsync();
        if (siteKey is { Length: > 0 } && site is null)
        {
            Console.Error.WriteLine($"[export-idealist] No site with key \"{siteKey}\". "
                                  + $"Known keys: {string.Join(", ", await db.Sites.Select(s => s.Key).ToListAsync())}");
            return 1;
        }

        var exporter = new IdeaListExporter(db, scope.ServiceProvider.GetRequiredService<IMediaStore>());
        var options = new IdeaListExportOptions
        {
            SlugPrefix = slugPrefix,
            IncludeMedia = includeMedia,
        };

        IdeaListExportResult result;
        if (dryRun)
        {
            result = await exporter.ExportAsync(null, site?.Id, options, MakeLog("export-idealist"));
            Console.WriteLine($"[export-idealist] DRY RUN — would write {outPath}: {result.List.Pages.Count} page(s), "
                             + $"{result.List.ComponentMetadata.Count} component metadata row(s), {result.List.Settings.Count} setting(s), "
                             + $"{result.List.Packages.Count} package(s).");
            foreach (var p in result.List.Pages.Take(10)) Console.WriteLine($"[export-idealist]   page /{p.Slug}");
            if (result.List.Pages.Count > 10) Console.WriteLine($"[export-idealist]   … and {result.List.Pages.Count - 10} more");
            return 0;
        }

        // The .idealist bytes are exactly what the exporter writes into the zip (HOUSE-LAW-5);
        // ArtifactWriter only owns placing the file: folder created, atomic write, and — as before —
        // an existing file at the exact path the caller named is overwritten.
        IdeaListExportResult? written = null;
        await ArtifactWriter.WriteZipAsync(
            Path.GetDirectoryName(outPath)!, Path.GetFileName(outPath),
            async (zip, _) => written = await exporter.ExportAsync(zip, site?.Id, options, MakeLog("export-idealist")),
            new ArtifactOptions { Existing = ExistingArtifact.Overwrite, SanitizeName = false });
        result = written!;

        Console.WriteLine($"[export-idealist] {result.List.Pages.Count} page(s), {result.List.ComponentMetadata.Count} component metadata row(s), "
                         + $"{result.List.Settings.Count} setting(s), {result.List.Packages.Count} package(s), {result.MediaUploaded} media item(s).");
        Console.WriteLine($"[export-idealist] wrote {outPath} ({IdeaListExporter.Describe(new FileInfo(outPath).Length)}, {result.MediaUploaded} media payload(s)).");
        return result.Ok ? 0 : 1;
    }

    internal static IIdeaListPackageResolver PackageResolverFor(string[] args, IServiceProvider services)
    {
        var dirs = ArgValues(args, "--packages-dir");
        var env = services.GetRequiredService<IHostEnvironment>();
        var configuration = services.GetRequiredService<IConfiguration>();
        return IdeaListResolverFactory.Build(dirs, env.ContentRootPath, configuration);
    }

    internal static IdeaListImporter.Log MakeLog(string tag) => (msg, isError) =>
    {
        if (isError) Console.Error.WriteLine($"[{tag}] {msg}");
        else Console.WriteLine($"[{tag}] {msg}");
    };

    internal static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
    }

    /// <summary>Every occurrence of a repeatable <c>--flag value</c> pair, in order.</summary>
    internal static List<string> ArgValues(string[] args, string name)
    {
        var values = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name && !args[i + 1].StartsWith("--"))
                values.Add(args[i + 1]);
        return values;
    }
}
