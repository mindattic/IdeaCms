using System.IO.Compression;
using IdeaCms.Core.Data;
using IdeaCms.Core.Portability;
using IdeaCms.Core.Services;
using MindAttic.Media;

namespace IdeaCms.Blazor.Cli;

/// <summary>
/// CLI mode: <c>--import-idealist &lt;file&gt;</c>. Applies an idealist produced by <see
/// cref="ExportIdeaListCli"/> or <see cref="ComposeIdeaListCli"/> to this environment.
/// <para>
/// Argument parsing and console reporting only — the work is <see cref="IdeaListImporter"/> in Core.
/// See M.IDEA-§4.9.
/// </para>
/// Usage: <c>dotnet run --project src/IdeaCms.Blazor -- --import-idealist site.idealist
/// [--into-site &lt;key&gt;] [--dry-run] [--untrusted] [--prune] [--packages-dir &lt;dir&gt;]</c>
/// </summary>
public static class ImportIdeaListCli
{
    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        var source = ExportIdeaListCli.ArgValue(args, "--import-idealist");
        if (string.IsNullOrWhiteSpace(source))
        {
            Console.Error.WriteLine("[import-idealist] No input file given. Usage: --import-idealist <file.idealist> [--into-site <key>] [--dry-run] [--untrusted] [--prune] [--packages-dir <dir>]");
            return 1;
        }
        var inPath = Path.GetFullPath(source);
        if (!File.Exists(inPath))
        {
            Console.Error.WriteLine($"[import-idealist] File not found: {inPath}");
            return 1;
        }

        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(inPath);
        }
        catch (InvalidDataException ex)
        {
            // A CLI reports a bad file; it does not stack-trace at the operator.
            Console.Error.WriteLine($"[import-idealist] Not a readable archive: {inPath} ({ex.Message})");
            return 1;
        }
        using var _ = zip;

        var (list, manifestError) = await IdeaListImporter.ReadManifestAsync(zip);
        if (list is null)
        {
            Console.Error.WriteLine($"[import-idealist] {manifestError}");
            return 1;
        }

        var options = new IdeaListImportOptions
        {
            DryRun = args.Contains("--dry-run"),
            ForceUntrusted = args.Contains("--untrusted"),
            Prune = args.Contains("--prune"),
            IntoSiteKey = ExportIdeaListCli.ArgValue(args, "--into-site"),
        };

        Console.WriteLine($"[import-idealist] {Path.GetFileName(inPath)} — exported {list.ExportedUtc:u} from {list.ExportedFrom ?? "?"}: "
                        + $"{list.Packages.Count} package(s), {list.Pages.Count} page(s), {list.Media.Count} media, {list.Settings.Count} setting(s).");
        if (options.DryRun) Console.WriteLine("[import-idealist] DRY RUN — nothing is written.");

        await using var scope = services.CreateAsyncScope();
        var importer = new IdeaListImporter(
            scope.ServiceProvider.GetRequiredService<CmsDbContext>(),
            scope.ServiceProvider.GetRequiredService<IMediaStore>(),
            scope.ServiceProvider.GetRequiredService<IPackageInstallService>(),
            ExportIdeaListCli.PackageResolverFor(args, scope.ServiceProvider));

        IdeaListImportResult result;
        try
        {
            result = await importer.ImportAsync(zip, list, options, ExportIdeaListCli.MakeLog("import-idealist"));
        }
        catch (IdeaListImportException ex)
        {
            Console.Error.WriteLine($"[import-idealist] {ex.Message}");
            foreach (var reason in ex.Reasons) Console.Error.WriteLine($"[import-idealist]   - {reason}");
            return 1;
        }

        if (result.Error is { Length: > 0 } error)
        {
            Console.Error.WriteLine($"[import-idealist] {error}");
            return 1;
        }
        return result.Ok ? 0 : 1;
    }
}
