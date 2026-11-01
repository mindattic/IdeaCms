using IdeaCms.Abstractions;
using IdeaCms.Core.Rendering;
using IdeaCms.Packaging;

namespace IdeaCms.Core.Discovery;

/// <summary>
/// A no-database content source for the Test Harness (<c>ma-idea preview</c>): it reads every
/// <c>.idea</c> in a local folder, validates each with the SAME <see cref="ManifestValidator"/> the host
/// uses (so preview rejects exactly what install would), extracts it via the SAME
/// <see cref="IPackageExtractor"/> (so the ALC type-resolver and the <c>/_ideas</c> asset route work
/// identically), and emits the SAME <c>Origin=Package</c> <see cref="ContentDescriptor"/>s
/// <see cref="DiscoveryService"/> produces — including the manifest css/scripts/uses surfaced into
/// <see cref="ContentDescriptor.Extra"/>. So a page previews against its real, by-string dependencies
/// with no SQL Server in the loop. Implements the same <see cref="ICmsContentSource"/> as compiled discovery.
/// </summary>
public sealed class LocalFolderPackageSource(string folder, IPackageExtractor extractor) : ICmsContentSource
{
    public string Name => "local-folder";
    public ContentOrigin Origin => ContentOrigin.Package;
    public int Priority => 50;

    public IEnumerable<ContentDescriptor> Discover()
    {
        var result = new List<ContentDescriptor>();
        if (!Directory.Exists(folder)) return result;

        foreach (var file in Directory.EnumerateFiles(folder, "*.idea").OrderBy(f => f, StringComparer.Ordinal))
        {
            using var reader = IdeaArchiveReader.Open(file);
            if (!reader.TryReadManifest(out var m, out _) || m is null) continue;
            if (!ManifestValidator.Validate(m, reader.BinEntries()).IsValid) continue;   // reject what the host rejects
            if (!Enum.TryParse<ContentKind>(m.Category, ignoreCase: true, out var kind)) continue;

            // Extract bin/ + wwwroot/ so the ALC resolver loads package types and /_ideas serves assets.
            extractor.Extract(reader, m.Category, m.Key, m.Version);

            result.Add(new ContentDescriptor
            {
                Kind = kind, Key = m.Key, Version = m.Version, DisplayName = m.DisplayName,
                Category = m.Category, Origin = ContentOrigin.Package, Priority = Priority,
                Strategy = RenderStrategy.ClrType,
                RenderMode = string.Equals(m.RenderMode, "Static", StringComparison.OrdinalIgnoreCase)
                    ? CmsRenderMode.Static : CmsRenderMode.InteractiveServer,
                Scope = string.Equals(m.Scope, "Global", StringComparison.OrdinalIgnoreCase)
                    ? PlacementScope.Global : PlacementScope.Placeable,
                ClrTypeName = m.EntryType, AssemblyName = m.AssemblyName,
                AssetMount = $"/_ideas/{m.Category}/{m.Key}/{m.Version}",
                Extra = ManifestAssetPacker.PackExtra(m),
            });
        }
        return result;
    }
}
