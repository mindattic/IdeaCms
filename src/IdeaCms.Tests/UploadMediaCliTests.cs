using Microsoft.Extensions.DependencyInjection;
using IdeaCms.Blazor.Cli;
using MindAttic.Media;

namespace IdeaCms.Tests;

/// <summary>
/// M.IDEA-§4.11 / M.IDEA-US-I5: <c>--upload-media</c> streams local files into whichever media store is
/// configured. These assert the CLI contract — which files it picks up, what it labels them, and that
/// it hands the store a stream rather than a buffer.
/// </summary>
[TestFixture]
public class UploadMediaCliTests
{
    private sealed class RecordingMediaStore : IMediaStore
    {
        public readonly List<MediaItem> Items = [];
        public readonly List<long> UploadedLengths = [];

        public Task<MediaItem> UploadAsync(Stream content, string fileName, string contentType,
            int? tenantId = null, string folder = "", string mediaType = "", int? width = null,
            int? height = null, string? notes = null, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            content.CopyTo(ms);
            UploadedLengths.Add(ms.Length);
            var item = new MediaItem
            {
                Uid = Guid.NewGuid(), FileName = fileName, ContentType = contentType,
                Folder = folder, MediaType = mediaType, Notes = notes, SizeBytes = ms.Length,
            };
            Items.Add(item);
            return Task.FromResult(item);
        }

        public Task<IReadOnlyList<MediaItem>> ListAsync(int? tenantId = null, string? folder = null,
            string? mediaType = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MediaItem>>(Items);

        public Task<MediaItem?> GetMetaAsync(Guid uid, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(i => i.Uid == uid));

        public Task<(MediaItem Meta, Stream Content)?> GetAsync(Guid uid, CancellationToken ct = default)
            => Task.FromResult<(MediaItem, Stream)?>(null);

        public Task<bool> DeleteAsync(Guid uid, CancellationToken ct = default)
            => Task.FromResult(Items.RemoveAll(i => i.Uid == uid) > 0);
    }

    private string dir = "";
    private RecordingMediaStore store = null!;
    private ServiceProvider services = null!;

    [SetUp]
    public void SetUp()
    {
        dir = Path.Combine(Path.GetTempPath(), "ma-upload-cli", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        store = new RecordingMediaStore();
        services = new ServiceCollection().AddSingleton<IMediaStore>(store).BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        services.Dispose();
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private string WriteFile(string name, int bytes = 64)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Test]
    public async Task UploadsAVideoWithTheRightContentTypeAndMediaType()
    {
        var path = WriteFile("feature.mp4", 4096);

        var exit = await UploadMediaCli.RunAsync(["--upload-media", path, "--folder", "site"], services);

        Assert.That(exit, Is.Zero);
        Assert.That(store.Items, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(store.Items[0].FileName, Is.EqualTo("feature.mp4"));
            Assert.That(store.Items[0].ContentType, Is.EqualTo("video/mp4"));
            Assert.That(store.Items[0].MediaType, Is.EqualTo("video"));
            Assert.That(store.Items[0].Folder, Is.EqualTo("site"));
            Assert.That(store.UploadedLengths[0], Is.EqualTo(4096));
        });
    }

    [Test]
    public async Task UploadsEveryFileUpToTheNextFlag()
    {
        var a = WriteFile("a.png");
        var b = WriteFile("b.pdf");
        var c = WriteFile("c.webm");

        var exit = await UploadMediaCli.RunAsync(
            ["--upload-media", a, b, c, "--media-type", "gallery"], services);

        Assert.That(exit, Is.Zero);
        Assert.That(store.Items.Select(i => i.FileName), Is.EqualTo(new[] { "a.png", "b.pdf", "c.webm" }));
        Assert.Multiple(() =>
        {
            Assert.That(store.Items.Select(i => i.ContentType),
                Is.EqualTo(new[] { "image/png", "application/pdf", "video/webm" }));
            Assert.That(store.Items.Select(i => i.MediaType),
                Is.All.EqualTo("gallery"), "an explicit --media-type overrides the sniffed kind");
        });
    }

    [Test]
    public async Task DryRunUploadsNothing()
    {
        var path = WriteFile("feature.mp4");

        var exit = await UploadMediaCli.RunAsync(["--upload-media", path, "--dry-run"], services);

        Assert.That(exit, Is.Zero);
        Assert.That(store.Items, Is.Empty);
    }

    [Test]
    public async Task MissingFileFailsBeforeUploadingAnything()
    {
        var real = WriteFile("real.png");
        var ghost = Path.Combine(dir, "ghost.png");

        var exit = await UploadMediaCli.RunAsync(["--upload-media", real, ghost], services);

        Assert.That(exit, Is.EqualTo(1));
        Assert.That(store.Items, Is.Empty, "a bad path must not leave a half-done upload behind");
    }

    [Test]
    public async Task NoFilesIsAnError()
    {
        var exit = await UploadMediaCli.RunAsync(["--upload-media", "--folder", "site"], services);

        Assert.That(exit, Is.EqualTo(1));
        Assert.That(store.Items, Is.Empty);
    }

    [Test]
    public async Task UnknownExtensionFallsBackToOctetStream()
    {
        var path = WriteFile("archive.qqq");

        await UploadMediaCli.RunAsync(["--upload-media", path], services);

        Assert.That(store.Items[0].ContentType, Is.EqualTo("application/octet-stream"));
        Assert.That(store.Items[0].MediaType, Is.EqualTo("file"));
    }
}
