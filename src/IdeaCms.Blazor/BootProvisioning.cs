using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using IdeaCms.Core.Data;
using IdeaCms.Core.Portability;
using IdeaCms.Core.Services;
using MindAttic.Media;

namespace IdeaCms.Blazor;

/// <summary>
/// Decides "vanilla" vs. "custom instance" at boot, and applies whichever it is.
/// <para>
/// Vanilla — no <c>IdeaCms:Idealist</c> / <c>IDEACMS_IDEALIST</c> configured — installs every first-party
/// <c>.idea</c> physically present in <c>library/</c>, best-effort, exactly as this codebase always did.
/// </para>
/// <para>
/// Custom — a path is configured — applies that <c>.idealist</c> via <see cref="IdeaListImporter"/> and
/// deliberately does NOT catch any exception: a curated instance that cannot fully provision itself
/// (a missing package, an unmet <c>Uses[]</c>, a malformed manifest) should abort startup rather than
/// come up half-done.
/// </para>
/// Extracted from <c>Program.cs</c>'s top-level statements so both boot paths are unit-testable.
/// </summary>
public static class BootProvisioning
{
    public static async Task ApplyAsync(
        IServiceProvider sp, string contentRootPath, IConfiguration configuration,
        TextWriter? outWriter = null, TextWriter? errWriter = null, CancellationToken ct = default)
    {
        var stdout = outWriter ?? Console.Out;
        var stderr = errWriter ?? Console.Error;

        var idealistPath = configuration["IdeaCms:Idealist"] ?? Environment.GetEnvironmentVariable("IDEACMS_IDEALIST");
        if (!string.IsNullOrWhiteSpace(idealistPath))
        {
            await ApplyCustomAsync(sp, contentRootPath, configuration, idealistPath, stdout, stderr, ct);
        }
        else
        {
            await ApplyVanillaAsync(sp, contentRootPath, stdout, stderr, ct);
        }
    }

    private static async Task ApplyCustomAsync(
        IServiceProvider sp, string contentRootPath, IConfiguration configuration, string idealistPath,
        TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var fullPath = Path.IsPathRooted(idealistPath) ? idealistPath : Path.Combine(contentRootPath, idealistPath);
        if (!File.Exists(fullPath))
            throw new InvalidOperationException($"IdeaCms:Idealist names \"{idealistPath}\" but {fullPath} does not exist.");

        using var zip = ZipFile.OpenRead(fullPath);
        var (list, error) = await IdeaListImporter.ReadManifestAsync(zip);
        if (list is null)
            throw new InvalidOperationException($"{fullPath}: {error}");

        var dirs = configuration.GetSection("IdeaCms:PackagesDirs").Get<string[]>() ?? [];
        var resolver = IdeaListResolverFactory.Build(dirs, contentRootPath, configuration);

        var importer = new IdeaListImporter(
            sp.GetRequiredService<CmsDbContext>(), sp.GetRequiredService<IMediaStore>(),
            sp.GetRequiredService<IPackageInstallService>(), resolver);

        // No try/catch here — a thrown IdeaListImportException (or anything else) propagates out of
        // ApplyAsync and aborts startup. That is the intended failure mode for a curated instance.
        await importer.ImportAsync(zip, list, new IdeaListImportOptions(), (msg, isError) =>
        {
            if (isError) stderr.WriteLine($"[idealist] {msg}");
            else stdout.WriteLine($"[idealist] {msg}");
        }, ct);
        stdout.WriteLine($"[idealist] applied {Path.GetFileName(fullPath)}.");
    }

    private static async Task ApplyVanillaAsync(
        IServiceProvider sp, string contentRootPath, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        var libraryDir = Path.Combine(contentRootPath, "library");
        if (!Directory.Exists(libraryDir)) return;

        var seeder = sp.GetRequiredService<IPackageInstallService>();
        foreach (var file in Directory.EnumerateFiles(libraryDir, "*.idea").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                await using var bytes = File.OpenRead(file);
                var plan = await seeder.InstallAsync(bytes, allowOverride: false, ct);
                stdout.WriteLine($"[library] {Path.GetFileName(file)} -> {plan.Action}");
            }
            catch (Exception ex) { stderr.WriteLine($"[library] {Path.GetFileName(file)} FAILED: {ex.Message}"); }
        }
    }
}
