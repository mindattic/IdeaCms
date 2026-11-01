using System.Text;
using IdeaCms.Core.Services;

namespace IdeaCms.Tests.Packaging;

[TestFixture]
public class LocalFilePackageBlobStoreTests
{
    private string _root = "";

    [SetUp]
    public void SetUp() => _root = Path.Combine(Path.GetTempPath(), "ma-blob-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    [Test]
    public async Task Save_Then_Open_RoundTripsBytes_AndReturnsConventionPath()
    {
        var store = new LocalFilePackageBlobStore(_root);
        var bytes = Encoding.UTF8.GetBytes("idea-package-bytes");

        var blobPath = await store.SaveAsync("Plugin", "ui.tooltip", 3, bytes);
        Assert.That(blobPath, Is.EqualTo("Plugin/ui.tooltip/3.idea"));

        Assert.That(await store.ExistsAsync(blobPath), Is.True);
        await using var s = await store.OpenAsync(blobPath);
        Assert.That(s, Is.Not.Null);
        using var ms = new MemoryStream();
        await s!.CopyToAsync(ms);
        Assert.That(ms.ToArray(), Is.EqualTo(bytes));
    }

    [Test]
    public async Task Open_MissingBlob_ReturnsNull()
    {
        var store = new LocalFilePackageBlobStore(_root);
        Assert.That(await store.OpenAsync("Plugin/none/1.idea"), Is.Null);
        Assert.That(await store.ExistsAsync("Plugin/none/1.idea"), Is.False);
    }

    [Test]
    public void Open_PathEscapingRoot_ResolvesToNull_NotAnEscape()
    {
        var store = new LocalFilePackageBlobStore(_root);
        Assert.Multiple(() =>
        {
            Assert.That(store.ExistsAsync("../../etc/passwd").Result, Is.False);
            Assert.That(store.OpenAsync("../../etc/passwd").Result, Is.Null);
        });
    }
}
