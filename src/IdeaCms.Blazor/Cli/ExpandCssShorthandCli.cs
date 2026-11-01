using IdeaCms.Core.Services;

namespace IdeaCms.Blazor.Cli;

/// <summary>
/// CLI mode: <c>--expand-css-shorthand</c>. One-time (re-runnable) migration that runs every checked-in
/// library <c>.css</c> file through <see cref="CssConflictMerger"/> (M.IDEA-§4.7) — the same engine that
/// normalizes Untrusted PageCss at save time — so shorthand properties (margin/padding/border/etc.) are
/// expanded to their real longhand form wherever a same-selector rule actually overrides part of one,
/// and any duplicate-selector blocks a file has accumulated over time collapse into one, cancelling the
/// older, now-superseded declarations. box-shadow/text-shadow are left as a single atomic value (CSS
/// defines no real longhand for either). Plain <c>.css</c> files, not <c>Page.PageCss</c> rows — this
/// touches disk, not the database.
/// <para>
/// Known, accepted side effect: AngleSharp.Css's serializer re-canonicalizes EVERY value it touches, not
/// just conflicting ones — named/hex colors become <c>rgba(...)</c>, and equal-valued longhands recombine
/// back into their compact shorthand form. Review the diff expecting that cosmetic reformatting, not just
/// the intended structural changes.
/// </para>
/// Usage: <c>dotnet run --project src/IdeaCms.Blazor -- --expand-css-shorthand [--path library]
/// [--dry-run]</c>
/// </summary>
public static class ExpandCssShorthandCli
{
    public static Task<int> RunAsync(string[] args)
    {
        var dryRun = args.Contains("--dry-run");
        var root = ArgValue(args, "--path") ?? "library";

        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"[expand-css-shorthand] Path not found: {root}");
            return Task.FromResult(1);
        }

        if (dryRun) Console.WriteLine("[expand-css-shorthand] DRY RUN — no files written.");

        var files = Directory.EnumerateFiles(root, "*.css", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int changed = 0, unchanged = 0, failed = 0;
        foreach (var file in files)
        {
            var before = File.ReadAllText(file);
            string after;
            try
            {
                after = CssConflictMerger.Normalize(before);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[expand-css-shorthand]   ! {file}: {ex.Message} — left unchanged.");
                failed++;
                continue;
            }

            if (after == before)
            {
                unchanged++;
                continue;
            }

            Console.WriteLine($"[expand-css-shorthand]   ~ {file} ({before.Length:N0} -> {after.Length:N0} chars)");
            changed++;
            if (!dryRun) File.WriteAllText(file, after);
        }

        Console.WriteLine($"[expand-css-shorthand] Done. changed={changed} unchanged={unchanged} failed={failed}" +
                           (dryRun ? " (dry run — nothing written)" : ""));
        return Task.FromResult(failed > 0 ? 1 : 0);
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
