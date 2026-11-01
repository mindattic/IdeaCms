using System.Collections.Concurrent;
using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Core.Discovery;

/// <summary>
/// Phase-1 type resolver over the default load context. The Phase-5 ALC-aware resolver replaces this
/// behind the same <see cref="ITypeResolver"/> interface — no other code learns about assembly loading.
/// A miss returns null so the renderer degrades to a placeholder instead of crashing.
/// </summary>
public sealed class DefaultTypeResolver : ITypeResolver
{
    private readonly ConcurrentDictionary<string, Type?> _cache = new();

    public Type? Resolve(ContentDescriptor descriptor)
    {
        if (string.IsNullOrEmpty(descriptor.ClrTypeName))
            return null;
        var cacheKey = $"{descriptor.ClrTypeName},{descriptor.AssemblyName}";
        return _cache.GetOrAdd(cacheKey, _ => ResolveCore(descriptor));
    }

    private static Type? ResolveCore(ContentDescriptor d)
    {
        if (!string.IsNullOrEmpty(d.AssemblyName))
        {
            var qualified = Type.GetType($"{d.ClrTypeName}, {d.AssemblyName}", throwOnError: false);
            if (qualified is not null) return qualified;
        }
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.IsNullOrEmpty(d.AssemblyName) && asm.GetName().Name != d.AssemblyName) continue;
            var t = asm.GetType(d.ClrTypeName!, throwOnError: false);
            if (t is not null) return t;
        }
        // Last resort: scan everything (covers types whose assembly name drifted).
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(d.ClrTypeName!, throwOnError: false);
            if (t is not null) return t;
        }
        return null;
    }
}
