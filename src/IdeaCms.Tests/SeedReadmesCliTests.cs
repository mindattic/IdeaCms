using IdeaCms.Blazor.Cli;

namespace IdeaCms.Tests;

/// <summary>
/// <c>--seed from-md</c> reads each project's README from the MindAttic workspace. The paths are relative
/// to that workspace (the parent of this checkout, or <c>MINDATTIC_WORKSPACE</c>), never a hard-coded
/// drive, and UiUx (archived) lives on as MindAttic.Components.
/// </summary>
[TestFixture]
public class SeedReadmesCliTests
{
    [Test]
    public void EveryReadmePathIsRelativeToTheWorkspace()
    {
        Assert.Multiple(() =>
        {
            foreach (var p in SeedReadmesCli.Projects)
            {
                Assert.That(Path.IsPathRooted(p.RelativePath), Is.False, $"{p.Slug}: {p.RelativePath} is absolute");
                Assert.That(p.ReadmePath, Does.StartWith(SeedReadmesCli.WorkspaceRoot), p.Slug);
            }
        });
    }

    [Test]
    public void TheWorkspaceIsTheParentOfThisCheckout()
    {
        var root = SeedReadmesCli.ResolveWorkspaceRoot();
        if (Environment.GetEnvironmentVariable("MINDATTIC_WORKSPACE") is { Length: > 0 })
            Assert.Ignore("MINDATTIC_WORKSPACE overrides the discovered root.");

        var checkout = Directory.EnumerateDirectories(root)
            .Any(d => File.Exists(Path.Combine(d, "IdeaCms.slnx")));
        Assert.That(checkout, Is.True, $"{root} does not directly contain this checkout");
    }

    [Test]
    public void UiUxReadsTheComponentsReadme()
    {
        var uiux = SeedReadmesCli.Projects.Single(p => p.Slug == "uiux");

        Assert.That(uiux.RelativePath, Is.EqualTo("MindAttic.Components/README.md"));
    }
}
