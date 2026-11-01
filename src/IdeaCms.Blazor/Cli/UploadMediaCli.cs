using MindAttic.Media;

namespace IdeaCms.Blazor.Cli;

/// <summary>
/// CLI mode: <c>--upload-media &lt;path…&gt;</c>. Streams local files into the configured media store and
/// prints the tokens to paste into a page.
/// <para>
/// The Admin Media panel is the right tool for an image; it is the wrong tool for a 400 MB video, which
/// would have to cross a SignalR circuit first. This path reads the file off disk and streams it straight
/// at the backing store, so upload size is bounded by the store, not by the browser.
/// </para>
/// Usage: <c>dotnet run --project src/IdeaCms.Blazor -- --upload-media &lt;file&gt; [more files…]
/// [--folder site] [--media-type video] [--notes "…"] [--dry-run]</c>
/// </summary>
public static class UploadMediaCli
{
    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        var dryRun = args.Contains("--dry-run");
        var folder = ArgValue(args, "--folder") ?? "site";
        var mediaType = ArgValue(args, "--media-type");
        var notes = ArgValue(args, "--notes");

        var paths = FilesFrom(args);
        if (paths.Count == 0)
        {
            Console.Error.WriteLine("[upload-media] No files given. Usage: --upload-media <file> [more files…] [--folder site]");
            return 1;
        }

        var missing = paths.Where(p => !File.Exists(p)).ToList();
        if (missing.Count > 0)
        {
            foreach (var p in missing) Console.Error.WriteLine($"[upload-media] File not found: {p}");
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var media = scope.ServiceProvider.GetRequiredService<IMediaStore>();
        var failures = 0;

        foreach (var path in paths)
        {
            var info = new FileInfo(path);
            var contentType = ContentTypeFor(info.Extension);
            var kind = mediaType ?? KindFor(contentType);

            if (dryRun)
            {
                Console.WriteLine($"[upload-media] [DRY] {info.Name} ({Describe(info.Length)}, {contentType}) -> folder \"{folder}\", type \"{kind}\"");
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(path);
                var item = await media.UploadAsync(
                    stream, info.Name, contentType, folder: folder, mediaType: kind, notes: notes);

                Console.WriteLine($"[upload-media]   + {info.Name} ({Describe(item.SizeBytes)}) -> {item.Uid}");
                Console.WriteLine($"[upload-media]     {TokenFor(item)}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[upload-media]   ! {info.Name}: {ex.Message}");
                failures++;
            }
        }

        return failures == 0 ? 0 : 1;
    }

    /// <summary>Every argument after <c>--upload-media</c> up to the next flag.</summary>
    private static List<string> FilesFrom(string[] args)
    {
        var start = Array.IndexOf(args, "--upload-media") + 1;
        var paths = new List<string>();
        for (var i = start; i > 0 && i < args.Length && !args[i].StartsWith("--"); i++)
            paths.Add(args[i]);
        return paths;
    }

    private static string TokenFor(MediaItem item) => item.ContentType switch
    {
        var t when t.StartsWith("image/", StringComparison.OrdinalIgnoreCase) =>
            $"<Component.MediaImage uid=\"{item.Uid}\" alt=\"\" />",
        var t when t.StartsWith("video/", StringComparison.OrdinalIgnoreCase) =>
            $"<video src=\"/_media/{item.Uid}\" controls preload=\"metadata\"></video>",
        var t when t.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) =>
            $"<audio src=\"/_media/{item.Uid}\" controls></audio>",
        _ => $"<Component.MediaLink uid=\"{item.Uid}\" label=\"{item.FileName}\" />",
    };

    private static string KindFor(string contentType) => contentType.Split('/')[0] switch
    {
        "image" => "image",
        "video" => "video",
        "audio" => "audio",
        _ => "file",
    };

    private static string ContentTypeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".mkv" => "video/x-matroska",
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".ogg" or ".oga" => "audio/ogg",
        ".wav" => "audio/wav",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".avif" => "image/avif",
        ".pdf" => "application/pdf",
        ".zip" => "application/zip",
        ".json" => "application/json",
        ".txt" or ".md" => "text/plain",
        ".css" => "text/css",
        _ => "application/octet-stream",
    };

    private static string Describe(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
