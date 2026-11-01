using IdeaCms.Abstractions;

namespace IdeaCms.Tests;

/// <summary>
/// PluginSlot — a globally-activated Plugin declares whether it renders before or after the theme/body.
/// Before this existed every plugin rendered before the body, so a footer plugin landed at the top of the
/// page. PageHost reads the slot off the TYPE (no instantiation); these cover that read and its default.
/// </summary>
[TestFixture]
public class PluginSlotTests
{
    private sealed class NoAttribute : PluginBase { }

    [Idea(DisplayName = "Header-ish")]
    private sealed class AttributeWithoutSlot : PluginBase { }

    [Idea(DisplayName = "Footer-ish", Slot = PluginSlot.AfterBody)]
    private sealed class AfterBody : PluginBase { }

    [Idea(DisplayName = "Explicit", Slot = PluginSlot.BeforeBody)]
    private sealed class ExplicitBeforeBody : PluginBase { }

    /// <summary>Mirrors PageHost.SlotOf — the type-only read the renderer performs.</summary>
    private static PluginSlot SlotOf(Type t) =>
        t.GetCustomAttributes(typeof(IdeaAttribute), inherit: false) is [IdeaAttribute a, ..]
            ? a.Slot
            : PluginSlot.BeforeBody;

    [Test]
    public void PluginWithNoIdeaAttribute_DefaultsToBeforeBody()
    {
        // Every plugin shipped before the slot existed rendered before the body; that must not change.
        Assert.That(SlotOf(typeof(NoAttribute)), Is.EqualTo(PluginSlot.BeforeBody));
    }

    [Test]
    public void PluginWithIdeaAttributeButNoSlot_DefaultsToBeforeBody()
    {
        Assert.That(SlotOf(typeof(AttributeWithoutSlot)), Is.EqualTo(PluginSlot.BeforeBody));
    }

    [Test]
    public void PluginDeclaringAfterBody_IsReadAsAfterBody()
    {
        Assert.That(SlotOf(typeof(AfterBody)), Is.EqualTo(PluginSlot.AfterBody));
    }

    [Test]
    public void PluginDeclaringBeforeBody_IsReadAsBeforeBody()
    {
        Assert.That(SlotOf(typeof(ExplicitBeforeBody)), Is.EqualTo(PluginSlot.BeforeBody));
    }

    [Test]
    public void SlotPartition_KeepsAuthorOrderWithinEachSlot()
    {
        // PageHost partitions one selection list into two render passes; a plugin must not overtake
        // another in the same slot just because a differently-slotted one sits between them.
        Type[] selection = [typeof(AttributeWithoutSlot), typeof(AfterBody), typeof(ExplicitBeforeBody)];

        var before = selection.Where(t => SlotOf(t) == PluginSlot.BeforeBody).ToArray();
        var after = selection.Where(t => SlotOf(t) == PluginSlot.AfterBody).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(new[] { typeof(AttributeWithoutSlot), typeof(ExplicitBeforeBody) }));
            Assert.That(after, Is.EqualTo(new[] { typeof(AfterBody) }));
        });
    }

    [Test]
    public void BeforeBodyIsOrdinalZero_SoAppendingNeverRenumbers()
    {
        // The enum is append-only (M.IDEA-LAW-2); pinning the ordinals guards a future addition from
        // silently changing what an already-packed .idea means.
        Assert.Multiple(() =>
        {
            Assert.That((int)PluginSlot.BeforeBody, Is.EqualTo(0));
            Assert.That((int)PluginSlot.AfterBody, Is.EqualTo(1));
        });
    }
}
