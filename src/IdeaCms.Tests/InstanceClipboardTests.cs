using MindAttic.Ideas.Abstractions;
using IdeaCms.Blazor.Services;
using IdeaCms.Core.Rendering;
using IdeaCms.Core.Services;

namespace IdeaCms.Tests;

/// <summary>M.IDEA-§4.5: configuration is shared by copying one instance onto another, never by a global layer.</summary>
[TestFixture]
public class InstanceClipboardTests
{
    private static SettingDescriptor S(string name, bool copyable = true) =>
        new(name, typeof(string), SettingValueKind.Text, name, null, null, 0, copyable, false, [], null);

    private static InstanceSettingsView View(string slug, ContentKind kind, string key, Dictionary<string, string?> values) =>
        new(1, slug, "tag:0:" + key, kind, key, key, [S("Radius"), S("Accent"), S("Uid", copyable: false)], values);

    [Test]
    public void Paste_ReplacesCopyableSettings_KeepsTargetContent_AndResetsWhatTheSourceLeftUnset()
    {
        var clip = new InstanceClipboard();
        clip.Copy(View("a", ContentKind.Component, "card", new() { ["Radius"] = "8px", ["Uid"] = "source-image" }));

        var pasted = clip.PasteOnto(View("b", ContentKind.Component, "card",
            new() { ["Accent"] = "red", ["Uid"] = "target-image" }));

        Assert.Multiple(() =>
        {
            Assert.That(pasted["Radius"], Is.EqualTo("8px"));
            Assert.That(pasted["Accent"], Is.Null, "unset on the source → back to default on the target");
            Assert.That(pasted["Uid"], Is.EqualTo("target-image"), "content (Copyable = false) never travels");
            Assert.That(clip.Current!.Values.ContainsKey("Uid"), Is.False);
        });
    }

    [Test]
    public void Paste_OnlyOntoTheSameCitizen()
    {
        var clip = new InstanceClipboard();
        Assert.That(clip.CanPasteOnto(ContentKind.Component, "card"), Is.False, "nothing copied yet");

        clip.Copy(View("a", ContentKind.Component, "card", new()));

        Assert.Multiple(() =>
        {
            Assert.That(clip.CanPasteOnto(ContentKind.Component, "CARD"), Is.True);
            Assert.That(clip.CanPasteOnto(ContentKind.Component, "hero"), Is.False);
            Assert.That(clip.CanPasteOnto(ContentKind.Plugin, "card"), Is.False);
        });
    }
}
