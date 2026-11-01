using System.Reflection;
using IdeaCms.Core.Services;

namespace IdeaCms.Tests;

/// <summary>
/// Contract guards for the Phase-2 admin services. The owner rule: content/pages are SOFT-disabled or
/// soft-deleted, never hard-removed (authored history + references must never dangle). These pin the API
/// shape so a future hard-delete can't slip in. (ContentLifecycleService.DeleteAsync is intentionally
/// present — it is reference-guarded and degrades compiled content to disable, not a hard removal.)
/// </summary>
[TestFixture]
public class AdminServiceContractTests
{
    private static string[] Methods<T>() =>
        typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ToArray();

    [Test]
    public void PageAdminService_HasSoftDelete_AndNoHardDelete()
    {
        var m = Methods<IPageAdminService>();
        Assert.That(m, Does.Contain(nameof(IPageAdminService.SoftDeleteAsync)));
        Assert.That(m, Has.None.EqualTo("DeleteAsync"));
        Assert.That(m.Where(n =>
            n.StartsWith("Hard", StringComparison.OrdinalIgnoreCase) ||
            n.StartsWith("Purge", StringComparison.OrdinalIgnoreCase) ||
            n.StartsWith("Remove", StringComparison.OrdinalIgnoreCase)), Is.Empty);
    }

    [Test]
    public void ContentLifecycleService_HasEnableAndGuardedDelete()
    {
        var m = Methods<IContentLifecycleService>();
        Assert.Multiple(() =>
        {
            Assert.That(m, Does.Contain(nameof(IContentLifecycleService.SetEnabledAsync)));
            Assert.That(m, Does.Contain(nameof(IContentLifecycleService.CanDeleteAsync)));
            Assert.That(m, Does.Contain(nameof(IContentLifecycleService.DeleteAsync)));   // reference-guarded, not a hard delete
        });
    }

    [Test]
    public void AdminInboxService_HasRaiseListResolve()
    {
        var m = Methods<IAdminInboxService>();
        Assert.Multiple(() =>
        {
            Assert.That(m, Does.Contain(nameof(IAdminInboxService.RaiseAsync)));
            Assert.That(m, Does.Contain(nameof(IAdminInboxService.ListAsync)));
            Assert.That(m, Does.Contain(nameof(IAdminInboxService.ResolveAsync)));
        });
    }
}
