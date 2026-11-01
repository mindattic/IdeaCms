using System.Collections.Concurrent;
using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Core.Discovery;

/// <summary>
/// The Phase-5 <see cref="ITypeResolver"/>: resolves PACKAGE citizens by loading their extracted entry
/// assembly through a per-package <see cref="CmsPackageLoadContext"/> (so package base types unify with the
/// host's), and DELEGATES every other descriptor to the default resolver — so existing compiled content
/// behaves byte-identically. A package whose bytes aren't extracted yet resolves to null (placeholder),
/// never a crash. Loaded types are cached for the process lifetime; this pins the (collectible) context,
/// which is intentional under the soft, effective-on-restart model — <see cref="System.Runtime.Loader.AssemblyLoadContext.Unload"/>
/// is never called.
/// </summary>
public sealed class AlcAwareTypeResolver(DefaultTypeResolver inner, IPackageExtractor extractor) : ITypeResolver
{
    private readonly ConcurrentDictionary<string, CmsPackageLoadContext> _contexts = new();
    private readonly ConcurrentDictionary<string, Type?> _typeCache = new();

    public Type? Resolve(ContentDescriptor descriptor)
    {
        if (descriptor.Origin != ContentOrigin.Package)
            return inner.Resolve(descriptor);

        if (string.IsNullOrEmpty(descriptor.ClrTypeName) || string.IsNullOrEmpty(descriptor.AssemblyName))
            return null;

        var entryPath = extractor.EntryDllPath(descriptor.Category, descriptor.Key, descriptor.Version, descriptor.AssemblyName);
        if (string.IsNullOrEmpty(entryPath) || !File.Exists(entryPath))
            return null;   // not extracted yet -> placeholder

        return _typeCache.GetOrAdd($"{descriptor.ClrTypeName}@{entryPath}", _ =>
        {
            try
            {
                var ctx = _contexts.GetOrAdd(entryPath, p => new CmsPackageLoadContext(p));
                var asm = ctx.LoadFromAssemblyPath(entryPath);
                return asm.GetType(descriptor.ClrTypeName!, throwOnError: false);
            }
            catch
            {
                return null;   // a broken/locked package degrades to a placeholder, never crashes a render
            }
        });
    }
}
