using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Core.Discovery;

/// <summary>
/// The unified in-memory catalog of active, enabled citizens. Populated by <c>DiscoveryService</c> at
/// boot (and after any package install in Phase 5). Lookups are by the pinned (Kind, Key, Version).
/// </summary>
public sealed class ContentCatalog(ITypeResolver resolver) : IContentCatalog
{
    // Single volatile reference so Load+LoadDisabled appear atomic to concurrent readers.
    private sealed record CatalogSnapshot(
        IReadOnlyList<ContentDescriptor> All,
        IReadOnlyList<(ContentKind Kind, string Key, int Version)> Disabled)
    {
        internal static readonly CatalogSnapshot Empty = new([], []);
    }

    private volatile CatalogSnapshot _snapshot = CatalogSnapshot.Empty;

    /// <summary>Replace the enabled-winners snapshot. Prefer <see cref="LoadSnapshot"/> to avoid a torn-state window.</summary>
    internal void Load(IEnumerable<ContentDescriptor> descriptors) =>
        _snapshot = _snapshot with { All = descriptors.ToArray() };

    /// <summary>Replace the disabled-identity snapshot. Prefer <see cref="LoadSnapshot"/> to avoid a torn-state window.</summary>
    internal void LoadDisabled(IEnumerable<(ContentKind Kind, string Key, int Version)> disabled) =>
        _snapshot = _snapshot with { Disabled = disabled.ToArray() };

    /// <summary>Replace both snapshots in a single atomic volatile write — eliminates the Disabled→Missing torn-state window.</summary>
    public void LoadSnapshot(
        IEnumerable<ContentDescriptor> enabled,
        IEnumerable<(ContentKind Kind, string Key, int Version)> disabled) =>
        _snapshot = new(enabled.ToArray(), disabled.ToArray());

    public IReadOnlyCollection<ContentDescriptor> All => _snapshot.All;

    public ContentDescriptor? Find(ContentKind kind, string key, int version) =>
        Find(_snapshot, kind, key, version);

    public ContentDescriptor? FindLatest(ContentKind kind, string key) =>
        FindLatest(_snapshot, kind, key);

    // Keys match case-INSENSITIVELY, as they do everywhere else a key is compared (the include-reference
    // parser, the manifest uses[] grammar, the delete guard). Discovery lowercases the keys it derives,
    // but a key can also arrive verbatim from a free-text field — Site.DefaultThemeKey in the Admin Sites
    // panel above all — and an ordinal compare made "Cyberspace" a MISSING theme for a whole site, which
    // then silently rendered under the bootstrap fallback.
    private static ContentDescriptor? Find(CatalogSnapshot snap, ContentKind kind, string key, int version) =>
        snap.All.FirstOrDefault(d => d.Kind == kind && KeyEquals(d.Key, key) && d.Version == version);

    private static ContentDescriptor? FindLatest(CatalogSnapshot snap, ContentKind kind, string key) =>
        snap.All
            .Where(d => d.Kind == kind && KeyEquals(d.Key, key))
            .OrderByDescending(d => d.Version)
            .FirstOrDefault();

    private static bool KeyEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public Type? ResolveType(ContentDescriptor descriptor) => resolver.Resolve(descriptor);

    /// <summary>
    /// Version-aware resolution: a pinned version is resolved exactly or reported Disabled/Missing — never
    /// silently promoted to the latest enabled version. A floating reference (version == null) resolves to
    /// the latest enabled version as before.
    /// </summary>
    public ResolvedContent ResolveTag(ContentKind kind, string key, int? version)
    {
        // ONE volatile read, then every lookup below runs against that same snapshot. The public
        // Find/FindLatest re-read the field, so calling them here would let a concurrent LoadSnapshot
        // pair a NEW enabled list with the OLD disabled list — reporting Missing for a citizen that
        // the reload had only just disabled (or vice versa).
        var snap = _snapshot;
        var desc = version is int v ? Find(snap, kind, key, v) : FindLatest(snap, kind, key);
        if (desc is not null)
        {
            var type = ResolveType(desc);
            return type is null
                ? new ResolvedContent(ContentResolution.Missing, null, desc)
                : new ResolvedContent(ContentResolution.Resolved, type, desc);
        }

        var known = version is int pinned
            ? snap.Disabled.Any(d => d.Kind == kind && KeyEquals(d.Key, key) && d.Version == pinned)
            : snap.Disabled.Any(d => d.Kind == kind && KeyEquals(d.Key, key));
        return new ResolvedContent(known ? ContentResolution.Disabled : ContentResolution.Missing, null, null);
    }
}
