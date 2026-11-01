using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Rendering;
using IdeaCms.Core.Services;

namespace IdeaCms.Blazor.Services;

/// <summary>A copied instance configuration: only its COPYABLE settings (content-like settings stay put).</summary>
public sealed record CopiedConfiguration(
    ContentKind Kind, string Key, string SourceLabel, IReadOnlyDictionary<string, string?> Values);

/// <summary>
/// The Admin "Copy Configuration" clipboard (M.IDEA-§4.5). Circuit-scoped, so a copy survives switching pages
/// and Admin tabs, and only ever pastes onto an instance of the SAME citizen (kind + key).
/// </summary>
public sealed class InstanceClipboard
{
    public CopiedConfiguration? Current { get; private set; }
    public event Action? Changed;

    public void Copy(InstanceSettingsView view)
    {
        var copyable = view.Schema.Where(d => d.Copyable).Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var values = view.Values.Where(kv => copyable.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        Current = new CopiedConfiguration(view.Kind, view.Key,
            $"{view.DisplayName} on /{view.PageSlug}", values);
        Changed?.Invoke();
    }

    public bool CanPasteOnto(ContentKind kind, string key) =>
        Current is { } c && c.Kind == kind && string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The target's values after a paste: every copyable setting takes the copied value (or reverts to
    /// default when the source left it unset); non-copyable settings keep the target's own values.
    /// </summary>
    public Dictionary<string, string?> PasteOnto(InstanceSettingsView target)
    {
        var result = new Dictionary<string, string?>(target.Values, StringComparer.OrdinalIgnoreCase);
        if (Current is not { } c) return result;
        foreach (var d in target.Schema.Where(d => d.Copyable))
            result[d.Name] = c.Values.TryGetValue(d.Name, out var v) ? v : null;
        return result;
    }
}
