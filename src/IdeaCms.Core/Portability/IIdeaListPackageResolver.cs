using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Core.Portability;

/// <summary>
/// Locates the `.idea` bytes for one <see cref="IdeaList.Packages"/> entry — the ONLY way
/// <see cref="IdeaListImporter"/> resolves a package (a `.idealist` is a pure pointer list now that
/// each `.idea` citizen can be distributed on its own, e.g. via NuGet). Typical composition: the local
/// `library/` folder first (<c>FileSystemIdeaListPackageResolver</c>, fast, no network), a NuGet-backed
/// resolver as fallback, composed via <c>CompositeIdeaListPackageResolver</c>.
/// </summary>
public interface IIdeaListPackageResolver
{
    /// <summary>Returns a readable stream positioned at 0, or null if this resolver cannot find a match.</summary>
    Task<Stream?> ResolveAsync(ContentKind kind, string key, int version, CancellationToken ct = default);
}
