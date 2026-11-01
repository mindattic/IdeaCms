using IdeaCms.Packaging;

namespace IdeaCms.Tests.Packaging;

[TestFixture]
public class PackageVersionResolverTests
{
    private const string DefaultSha = "sha-default";

    private static IdeaManifest Candidate(int version, string key = "ui.tooltip", string category = "Plugin") =>
        new() { ManifestVersion = 1, Category = category, Kind = "code", Key = key, Version = version,
                DisplayName = "Demo", Sdk = 1, EntryType = "X" };

    private static InstalledRef Installed(int version, bool enabled = true, bool active = false,
        string key = "ui.tooltip", string category = "Plugin", string sha = DefaultSha) =>
        new(category, key, version, enabled, active, sha);

    [Test]
    public void SameVersionAlreadyInstalled_IsNoOp()
    {
        var plan = PackageVersionResolver.Plan(Candidate(2), DefaultSha, [Installed(2, active: true)], compiledKeyExists: false, allowOverride: false);
        Assert.That(plan.Action, Is.EqualTo(InstallAction.NoOpAlreadyInstalled));
        Assert.That(plan.ShouldWrite, Is.False);
    }

    [Test]
    public void HigherVersion_Installs_MakesActive_AndDeactivatesPrior()
    {
        var plan = PackageVersionResolver.Plan(Candidate(3), DefaultSha, [Installed(2, active: true)], compiledKeyExists: false, allowOverride: false);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Action, Is.EqualTo(InstallAction.Install));
            Assert.That(plan.MakeActiveVersion, Is.True);
            Assert.That(plan.DeactivatePriorVersions, Has.Count.EqualTo(1));
            Assert.That(plan.DeactivatePriorVersions[0].Version, Is.EqualTo(2), "the prior active version is retained, not removed");
        });
    }

    [Test]
    public void LowerVersion_IsRejectedAsDowngrade()
    {
        var plan = PackageVersionResolver.Plan(Candidate(1), DefaultSha, [Installed(2, active: true)], compiledKeyExists: false, allowOverride: false);
        Assert.That(plan.Action, Is.EqualTo(InstallAction.RejectDowngrade));
        Assert.That(plan.ShouldWrite, Is.False);
    }

    [Test]
    public void CompiledCollision_WithoutOverride_IsBlocked()
    {
        var plan = PackageVersionResolver.Plan(Candidate(1), DefaultSha, [], compiledKeyExists: true, allowOverride: false);
        Assert.That(plan.Action, Is.EqualTo(InstallAction.Blocked));
        Assert.That(plan.ShouldWrite, Is.False);
    }

    [Test]
    public void CompiledCollision_WithOverride_RequiresConfirmation_ButWillWrite()
    {
        var plan = PackageVersionResolver.Plan(Candidate(1), DefaultSha, [], compiledKeyExists: true, allowOverride: true);
        Assert.That(plan.Action, Is.EqualTo(InstallAction.RequiresOverrideConfirmation));
        Assert.That(plan.ShouldWrite, Is.True, "override was confirmed, so the host applies it");
    }

    [Test]
    public void ResolveLatest_PicksMaxEnabled_IgnoresDisabled()
    {
        IReadOnlyList<InstalledRef> set = [Installed(1), Installed(3, enabled: false), Installed(2)];
        Assert.That(PackageVersionResolver.ResolveLatest("Plugin", "ui.tooltip", set), Is.EqualTo(2));
    }

    [Test]
    public void ResolveLatest_NullWhenNoneEnabled()
    {
        IReadOnlyList<InstalledRef> set = [Installed(1, enabled: false)];
        Assert.That(PackageVersionResolver.ResolveLatest("Plugin", "ui.tooltip", set), Is.Null);
    }

    [Test]
    public void DisabledHighVersion_DoesNotBlockReinstallOfThatExactVersion()
    {
        // A disabled v3 still occupies (key,v3): re-installing v3 is a no-op (the row exists), not a downgrade.
        var plan = PackageVersionResolver.Plan(Candidate(3), DefaultSha, [Installed(3, enabled: false, active: false)], compiledKeyExists: false, allowOverride: false);
        Assert.That(plan.Action, Is.EqualTo(InstallAction.NoOpAlreadyInstalled));
    }

    [Test]
    public void SameVersion_DifferentHash_IsAConflict_NotANoOp()
    {
        var plan = PackageVersionResolver.Plan(Candidate(2), "sha-different", [Installed(2, active: true, sha: DefaultSha)],
            compiledKeyExists: false, allowOverride: false);
        Assert.Multiple(() =>
        {
            Assert.That(plan.Action, Is.EqualTo(InstallAction.HashConflict));
            Assert.That(plan.ShouldWrite, Is.False);
        });
    }

    [Test]
    public void SameVersion_SameHash_IsStillAPlainNoOp()
    {
        var plan = PackageVersionResolver.Plan(Candidate(2), DefaultSha, [Installed(2, active: true, sha: DefaultSha)],
            compiledKeyExists: false, allowOverride: false);
        Assert.That(plan.Action, Is.EqualTo(InstallAction.NoOpAlreadyInstalled));
    }

    [Test]
    public void SameVersion_DifferentHash_WithAllowOverride_InstallsRatherThanConflicting()
    {
        // allowOverride is a deliberate rebuild-at-the-same-slot push — expected to differ in hash.
        var plan = PackageVersionResolver.Plan(Candidate(2), "sha-different", [Installed(2, active: true, sha: DefaultSha)],
            compiledKeyExists: false, allowOverride: true);
        Assert.That(plan.Action, Is.EqualTo(InstallAction.Install));
        Assert.That(plan.ShouldWrite, Is.True);
    }

    [Test]
    public void PreLockdown_ChangedSameVersion_OverwritesInsteadOfConflicting()
    {
        var plan = PackageVersionResolver.Plan(Candidate(2), "sha-different", [Installed(2, active: true, sha: DefaultSha)],
            compiledKeyExists: false, allowOverride: false, overwriteHashConflicts: true);
        Assert.That(plan.Action, Is.EqualTo(InstallAction.Install));
        Assert.That(plan.ShouldWrite, Is.True);
    }

    [Test]
    public void PreLockdown_IdenticalSameVersion_IsStillANoOp()
    {
        // Overwriting identical content on every boot would be pure churn.
        var plan = PackageVersionResolver.Plan(Candidate(2), DefaultSha, [Installed(2, active: true, sha: DefaultSha)],
            compiledKeyExists: false, allowOverride: false, overwriteHashConflicts: true);
        Assert.That(plan.Action, Is.EqualTo(InstallAction.NoOpAlreadyInstalled));
    }

    [Test]
    public void PreLockdown_StillRejectsADowngrade()
    {
        var plan = PackageVersionResolver.Plan(Candidate(1), DefaultSha, [Installed(2, active: true)],
            compiledKeyExists: false, allowOverride: false, overwriteHashConflicts: true);
        Assert.That(plan.Action, Is.EqualTo(InstallAction.RejectDowngrade));
    }
}
