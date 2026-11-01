using System.Reflection;
using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Core.Discovery;

/// <summary>
/// Phase-1 source: reflects referenced assemblies for types deriving from a kind base
/// (PageBase/PluginBase/ThemeBase/ComponentBase) and emits a <see cref="ContentDescriptor"/> per type
/// by CONVENTION — identity follows the locked tag form <c>IdeaCms.{Kind}.{Name}.V{n}</c>:
/// Kind from the base, Key from the namespace tail after <c>IdeaCms.{Kind}.</c>, Version from
/// the <c>Vn</c> class name. An optional <see cref="IdeaAttribute"/> overrides any of these. A type
/// that derives from a base but doesn't fit the convention (and has no [Idea]) is skipped — so the
/// host's own internal subclasses (FreeFormPage, MissingPageHost) are never registered.
/// The Phase-5 PackageContentSource implements the same <see cref="ICmsContentSource"/>.
/// </summary>
public sealed class CompiledContentSource(IEnumerable<Assembly> assemblies) : ICmsContentSource
{
    private readonly Assembly[] _assemblies = assemblies.Distinct().ToArray();

    public string Name => "compiled";
    public ContentOrigin Origin => ContentOrigin.Compiled;
    public int Priority => 100;

    public IEnumerable<ContentDescriptor> Discover()
    {
        foreach (var asm in _assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray(); }

            var mount = $"_content/{asm.GetName().Name}";
            foreach (var t in types)
            {
                if (t.IsAbstract || !t.IsClass) continue;
                if (KindOf(t) is not { } kind) continue;

                var attr = t.GetCustomAttribute<IdeaAttribute>();
                var key = attr?.Key ?? KeyFromNamespace(t, kind);
                var version = attr is { Version: > 0 } ? attr.Version : VersionFromTypeName(t.Name);
                if (key is null || version is null) continue; // doesn't fit the convention and not overridden

                yield return new ContentDescriptor
                {
                    Kind = kind,
                    Key = key,
                    Version = version.Value,
                    DisplayName = attr?.DisplayName ?? key,
                    Category = attr?.Category ?? "General",
                    Scope = attr?.Scope ?? PlacementScope.Placeable,
                    RenderMode = attr?.RenderMode ?? CmsRenderMode.InteractiveServer,
                    Strategy = RenderStrategy.ClrType,
                    ClrTypeName = t.FullName,
                    AssemblyName = asm.GetName().Name ?? "",
                    AssetMount = mount,
                    Origin = Origin,
                    Priority = Priority,
                };
            }
        }
    }

    private static ContentKind? KindOf(Type t) =>
        typeof(PageBase).IsAssignableFrom(t) ? ContentKind.Page
        : typeof(ThemeBase).IsAssignableFrom(t) ? ContentKind.Theme
        : typeof(PluginBase).IsAssignableFrom(t) ? ContentKind.Plugin
        : typeof(ComponentBase).IsAssignableFrom(t) ? ContentKind.Component
        : null;

    // Namespace must be IdeaCms.{Kind}.{KeyPath}; key = KeyPath lowercased (dots preserved).
    private static string? KeyFromNamespace(Type t, ContentKind kind)
    {
        var prefix = $"IdeaCms.{kind}.";
        var ns = t.Namespace;
        if (ns is null || !ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var tail = ns[prefix.Length..];
        return tail.Length == 0 ? null : tail.ToLowerInvariant();
    }

    // Version is the Vn class name (V1, V11, V21, ...).
    private static int? VersionFromTypeName(string name) =>
        name.Length > 1 && (name[0] is 'V' or 'v') && int.TryParse(name.AsSpan(1), out var v) ? v : null;
}
