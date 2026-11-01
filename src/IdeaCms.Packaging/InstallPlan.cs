namespace IdeaCms.Packaging;

/// <summary>What an install of a candidate package should do, given what is already installed.</summary>
public enum InstallAction
{
    /// <summary>A new (or higher) version — write the rows.</summary>
    Install,
    /// <summary>This exact (category,key,version) is already installed — nothing to do.</summary>
    NoOpAlreadyInstalled,
    /// <summary>The candidate version is below the highest installed version — whole-number versions only move forward.</summary>
    RejectDowngrade,
    /// <summary>A compiled citizen owns this key and the package did not request an override.</summary>
    Blocked,
    /// <summary>A compiled citizen owns this key and the package requested an override (admin already confirmed via allowOverride).</summary>
    RequiresOverrideConfirmation,
    /// <summary>This exact (category,key,version) is already installed, but with DIFFERENT content —
    /// a real conflict, not a harmless re-run. Rejected outright; an operator must resolve it.</summary>
    HashConflict,
}

/// <summary>
/// The pure decision of <see cref="PackageVersionResolver.Plan"/>. Carries the prior active versions to
/// flip to <c>IsActiveVersion=false</c> (retained, never deleted) so the host can apply it transactionally.
/// </summary>
public sealed record InstallPlan(
    InstallAction Action,
    string? Reason,
    bool MakeActiveVersion,
    IReadOnlyList<(string Category, string Key, int Version)> DeactivatePriorVersions)
{
    /// <summary>The host should write rows for these actions; the rest are no-op or hard-reject.</summary>
    public bool ShouldWrite => Action is InstallAction.Install or InstallAction.RequiresOverrideConfirmation;
}
