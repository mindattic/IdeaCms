using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using IdeaCms.Abstractions;
using IdeaCms.Core.Data;
using MindAttic.Media;

namespace IdeaCms.Blazor.Cli;

/// <summary>
/// CLI mode: <c>--extract-media</c>. Lifts inline <c>data:image/…;base64,…</c> images out of page bodies
/// into the managed media store, rewriting each <c>&lt;img&gt;</c> as
/// <c>&lt;Component.MediaImage uid="…" /&gt;</c>.
/// <para>
/// Base64-inlining was how the hand-authored MindAttic sites shipped images, and it is the single biggest
/// reason a page body runs to hundreds of kilobytes. Once an image is a managed asset the browser can cache
/// it, the same bytes can be shared across pages, and the body is markup again.
/// </para>
/// <para>
/// Identical bytes upload once: assets are keyed by SHA-256, so a logo repeated across ten pages becomes one
/// asset with ten references. Re-runnable — a page with no inline images is left untouched.
/// </para>
/// Usage: <c>dotnet run --project src/IdeaCms.Blazor -- --extract-media [--slug frontpage]
/// [--folder site] [--dry-run]</c>
/// </summary>
public static partial class ExtractMediaCli
{
    /// <summary>An &lt;img&gt; whose src is an inline base64 data URI. Attribute order is not assumed.</summary>
    [GeneratedRegex(
        """<img\b[^>]*?\bsrc\s*=\s*(?<q>["'])data:(?<mime>image/[a-zA-Z0-9.+-]+);base64,(?<data>[A-Za-z0-9+/=\s]+?)\k<q>[^>]*>""",
        RegexOptions.IgnoreCase)]
    private static partial Regex InlineImage();

    /// <summary>
    /// A CSS <c>url(data:…;base64,…)</c>. CSS cannot reference a component, so these rewrite to the raw
    /// <c>/_media/{uid}</c> endpoint rather than to a MediaImage tag.
    /// </summary>
    [GeneratedRegex(
        """url\(\s*(?<q>["']?)data:(?<mime>image/[a-zA-Z0-9.+-]+);base64,(?<data>[A-Za-z0-9+/=\s]+?)\k<q>\s*\)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex CssDataUrl();

    /// <summary>The nearest custom property or selector before a url(), used to name the extracted asset.</summary>
    [GeneratedRegex("""(?<name>--[A-Za-z0-9_-]+)\s*:\s*[^;{}]*$""")]
    private static partial Regex PrecedingCustomProperty();

    /// <summary>One attribute inside a tag, used to carry alt/title/class/width/height across the rewrite.</summary>
    [GeneratedRegex("""(?<name>[A-Za-z_:][-A-Za-z0-9_:.]*)\s*=\s*(?<q>["'])(?<value>[^"']*)\k<q>""")]
    private static partial Regex TagAttribute();

    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        var dryRun = args.Contains("--dry-run");
        var onlySlug = ArgValue(args, "--slug");
        var folder = ArgValue(args, "--folder") ?? "pages";

        if (dryRun) Console.WriteLine("[extract-media] DRY RUN — no uploads, no DB writes.");

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var media = scope.ServiceProvider.GetRequiredService<IMediaStore>();

        var query = db.Pages.IgnoreQueryFilters()
            .Where(p => p.Kind == PageKind.Data
                        && ((p.BodyHtml != null && p.BodyHtml.Contains("base64,"))
                            || (p.PageCss != null && p.PageCss.Contains("base64,"))));
        if (!string.IsNullOrWhiteSpace(onlySlug))
            query = query.Where(p => p.Slug == onlySlug);

        var pages = await query.ToListAsync();
        if (pages.Count == 0)
        {
            Console.WriteLine("[extract-media] No pages contain inline base64 images.");
            return 0;
        }

        // Reuse an asset when the same bytes appear again — in this field, another field, or another page.
        var byHash = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in await media.ListAsync(folder: folder))
            if (!string.IsNullOrEmpty(existing.Sha256))
                byHash[existing.Sha256] = existing.Uid;

        var stats = new Stats();

        foreach (var page in pages)
        {
            var bodyBefore = page.BodyHtml;
            var cssBefore = page.PageCss;

            // Markup: an <img> becomes a MediaImage component.
            var bodyAfter = await LiftAsync(bodyBefore, InlineImage(), page.Slug, folder, media, byHash, dryRun, stats,
                (match, uid, bytes) => BuildTag(uid, AltOf(match), ReadAttributes(match.Value)),
                match => AltOf(match));

            // Stylesheet: CSS cannot reference a component, so a data: url() becomes the raw media endpoint.
            var cssAfter = await LiftAsync(cssBefore, CssDataUrl(), page.Slug, folder, media, byHash, dryRun, stats,
                (match, uid, bytes) => $"url(/_media/{uid})",
                match => CssAssetNameFor(cssBefore!, match));

            if (dryRun) continue;

            var changed = false;
            if (bodyAfter != bodyBefore)
            {
                Console.WriteLine($"[extract-media]   ~ /{page.Slug} body: {bodyBefore!.Length:N0} -> {bodyAfter!.Length:N0} chars");
                page.BodyHtml = bodyAfter;
                changed = true;
            }
            if (cssAfter != cssBefore)
            {
                Console.WriteLine($"[extract-media]   ~ /{page.Slug} css:  {cssBefore!.Length:N0} -> {cssAfter!.Length:N0} chars");
                page.PageCss = cssAfter;
                changed = true;
            }
            if (!changed) continue;

            page.ModifiedUtc = DateTime.UtcNow;
            stats.PagesChanged++;
        }

        if (!dryRun && stats.PagesChanged > 0) await db.SaveChangesAsync();

        Console.WriteLine($"[extract-media] Done. pages={stats.PagesChanged} extracted={stats.Extracted} " +
                          $"reused={stats.Reused} failed={stats.Failed} lifted={stats.BytesLifted / 1024:N0} KB");
        return stats.Failed > 0 ? 1 : 0;
    }

    private sealed class Stats
    {
        public int Extracted, Reused, Failed, PagesChanged;
        public long BytesLifted;
    }

    /// <summary>
    /// Replace every base64 match in <paramref name="input"/> with a managed-media reference.
    /// <paramref name="build"/> renders the replacement text; <paramref name="nameOf"/> supplies the asset
    /// filename. Returns the input unchanged when there is nothing to lift.
    /// </summary>
    private static async Task<string?> LiftAsync(
        string? input, Regex pattern, string slug, string folder, IMediaStore media,
        Dictionary<string, Guid> byHash, bool dryRun, Stats stats,
        Func<Match, Guid, byte[], string> build, Func<Match, string> nameOf)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains("base64,", StringComparison.Ordinal)) return input;

        var index = 0;
        return await ReplaceAsync(input, pattern, async match =>
        {
            index++;
            byte[] bytes;
            try
            {
                // Data URIs are routinely wrapped across lines in an authored file.
                bytes = Convert.FromBase64String(Regex.Replace(match.Groups["data"].Value, @"\s+", ""));
            }
            catch (FormatException)
            {
                Console.Error.WriteLine($"[extract-media]   ! {slug} asset {index}: unreadable base64 — left inline.");
                stats.Failed++;
                return match.Value;
            }

            var mime = match.Groups["mime"].Value.ToLowerInvariant();
            var name = nameOf(match);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));

            if (byHash.TryGetValue(hash, out var known))
            {
                stats.Reused++;
                return build(match, known, bytes);
            }

            if (dryRun)
            {
                Console.WriteLine($"[extract-media] [DRY] {slug} asset {index}: {bytes.Length / 1024} KB {mime} \"{Truncate(name)}\"");
                stats.Extracted++;
                stats.BytesLifted += bytes.Length;
                return match.Value;
            }

            var fileName = FileNameFor(name, slug, index, mime);
            await using var stream = new MemoryStream(bytes);
            var item = await media.UploadAsync(stream, fileName, mime, folder: folder, mediaType: "image",
                notes: $"Extracted from page /{slug}");
            byHash[hash] = item.Uid;
            stats.Extracted++;
            stats.BytesLifted += bytes.Length;
            Console.WriteLine($"[extract-media]   + {fileName} ({bytes.Length / 1024} KB) -> {item.Uid}");
            return build(match, item.Uid, bytes);
        });
    }

    private static string AltOf(Match match)
    {
        var attrs = ReadAttributes(match.Value);
        return attrs.GetValueOrDefault("alt") ?? attrs.GetValueOrDefault("title") ?? "";
    }

    /// <summary>
    /// Name a CSS-extracted asset after the custom property it is assigned to (<c>--bg-abstract-dark</c> ->
    /// <c>bg-abstract-dark</c>), so Admin → Media reads as names rather than frontpage-3.png.
    /// </summary>
    private static string CssAssetNameFor(string css, Match match)
    {
        var lookBehind = css[Math.Max(0, match.Index - 200)..match.Index];
        var m = PrecedingCustomProperty().Match(lookBehind);
        return m.Success ? m.Groups["name"].Value.TrimStart('-') : "";
    }

    /// <summary>Regex.Replace with an async replacement callback (uploading is I/O).</summary>
    private static async Task<string> ReplaceAsync(string input, Regex regex, Func<Match, Task<string>> replacer)
    {
        var sb = new System.Text.StringBuilder();
        var last = 0;
        foreach (Match m in regex.Matches(input))
        {
            sb.Append(input, last, m.Index - last);
            sb.Append(await replacer(m));
            last = m.Index + m.Length;
        }
        sb.Append(input, last, input.Length - last);
        return sb.ToString();
    }

    private static Dictionary<string, string> ReadAttributes(string tag)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in TagAttribute().Matches(tag))
        {
            var name = m.Groups["name"].Value;
            if (name.Equals("src", StringComparison.OrdinalIgnoreCase)) continue;   // that is what we are replacing
            attrs[name] = m.Groups["value"].Value;
        }
        return attrs;
    }

    /// <summary>
    /// The replacement tag. Only attributes MediaImage actually understands are carried over — anything else
    /// would be silently dropped by the component, so leaving it out keeps the markup honest.
    /// </summary>
    private static string BuildTag(Guid uid, string alt, Dictionary<string, string> attrs)
    {
        var sb = new System.Text.StringBuilder($"""<Component.MediaImage uid="{uid}" """);
        if (!string.IsNullOrEmpty(alt)) sb.Append($"""alt="{Escape(alt)}" """);
        foreach (var name in new[] { "width", "height", "class" })
        {
            if (!attrs.TryGetValue(name, out var v) || string.IsNullOrWhiteSpace(v)) continue;
            var attr = name == "class" ? "cssClass" : name;
            sb.Append($"""{attr}="{Escape(v)}" """);
        }
        return sb.Append("/>").ToString();
    }

    private static string FileNameFor(string alt, string slug, int index, string mime)
    {
        var stem = string.IsNullOrWhiteSpace(alt) ? $"{slug}-{index}" : alt;
        var safe = new string(stem.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (safe.Contains("--")) safe = safe.Replace("--", "-");
        safe = safe.Trim('-');
        if (safe.Length == 0) safe = $"image-{index}";
        if (safe.Length > 60) safe = safe[..60].TrimEnd('-');
        return safe + ExtensionFor(mime);
    }

    private static string ExtensionFor(string mime) => mime switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        "image/avif" => ".avif",
        _ => ".bin",
    };

    private static string Escape(string s) => s.Replace("\"", "&quot;");

    private static string Truncate(string s) => s.Length <= 40 ? s : s[..40] + "…";

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
