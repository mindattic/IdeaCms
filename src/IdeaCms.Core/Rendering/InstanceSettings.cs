using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using IdeaCms.Abstractions;

namespace IdeaCms.Core.Rendering;

/// <summary>Well-known instance-settings slot names on a page (M.IDEA-§4.5).</summary>
public static class InstanceSlots
{
    public const string Theme = "theme";
    public const string Page = "page";
    public const string PluginPrefix = "plugin:";
    public static string Plugin(string key) => PluginPrefix + key.ToLowerInvariant();
}

public enum SettingValueKind { Bool, Text, Integer, Number, Choice }

/// <summary>One instance setting a citizen exposes: a typed, writable [Parameter] plus its [Setting] metadata.</summary>
public sealed record SettingDescriptor(
    string Name,
    Type ClrType,
    SettingValueKind ValueKind,
    string DisplayName,
    string? Description,
    string? Group,
    int Order,
    bool Copyable,
    bool Multiline,
    IReadOnlyList<string> Choices,
    string? DefaultValue);

/// <summary>Reflects a citizen type's instance settings. Cached per type; never throws.</summary>
public static class SettingsSchema
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<SettingDescriptor>> Cache = new();

    // Host-wired plumbing, never an instance setting even though it is a [Parameter].
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase) { "Body", "ChildContent" };

    public static IReadOnlyList<SettingDescriptor> For(Type type) => Cache.GetOrAdd(type, Build);

    public static SettingDescriptor? Find(Type type, string name) =>
        For(type).FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<SettingDescriptor> Build(Type type)
    {
        object? defaults = null;
        try { defaults = Activator.CreateInstance(type); } catch { /* no parameterless ctor: no defaults */ }

        var list = new List<SettingDescriptor>();
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanWrite || !p.CanRead || Reserved.Contains(p.Name)) continue;
            if (p.GetCustomAttribute<ParameterAttribute>() is not { CaptureUnmatchedValues: false }) continue;
            var meta = p.GetCustomAttribute<SettingAttribute>();
            if (meta?.Hidden == true) continue;
            var kind = KindOf(p.PropertyType);
            if (kind is null) continue;

            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            string? def = null;
            if (defaults is not null)
            {
                try { def = Format(p.GetValue(defaults)); } catch { }
            }
            list.Add(new SettingDescriptor(
                p.Name, p.PropertyType, kind.Value,
                meta?.DisplayName ?? Humanize(p.Name),
                meta?.Description, meta?.Group, meta?.Order ?? 0,
                meta?.Copyable ?? true, meta?.Multiline ?? false,
                t.IsEnum ? Enum.GetNames(t) : [],
                def));
        }
        return list
            .OrderBy(d => d.Group ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Order)
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SettingValueKind? KindOf(Type propertyType)
    {
        var t = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (t == typeof(bool)) return SettingValueKind.Bool;
        if (t == typeof(string)) return SettingValueKind.Text;
        if (t.IsEnum) return SettingValueKind.Choice;
        if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte)) return SettingValueKind.Integer;
        if (t == typeof(double) || t == typeof(float) || t == typeof(decimal)) return SettingValueKind.Number;
        return null;
    }

    /// <summary>The invariant string form of a setting value ("true", "0.5", enum name, text).</summary>
    public static string? Format(object? value) => value switch
    {
        null => null,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static string Humanize(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }
}

/// <summary>
/// Converts between the stored instance-settings JSON (a flat object of setting name to typed value) and
/// the string-form value maps Admin edits and the Blazor parameters a citizen is rendered with.
/// </summary>
public static class InstanceSettingsBinder
{
    /// <summary>Stored JSON → name/string-value map (case-insensitive). Malformed JSON reads as empty.</summary>
    public static Dictionary<string, string?> Parse(string? json)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return map;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj) return map;
            foreach (var (k, v) in obj)
            {
                map[k] = v switch
                {
                    null => null,
                    JsonValue jv when jv.TryGetValue<string>(out var s) => s,
                    _ => v.ToJsonString(),
                };
            }
        }
        catch (JsonException) { }
        return map;
    }

    /// <summary>
    /// Name/string-value map → stored JSON, typed by the citizen's schema (bools and numbers stay JSON
    /// primitives). Unknown names and unparsable values are dropped, so a stale or tampered value can never
    /// reach a render.
    /// </summary>
    public static string Serialize(Type citizenType, IReadOnlyDictionary<string, string?> values)
    {
        var lookup = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in values) lookup[kv.Key] = kv.Value;
        var obj = new JsonObject();
        foreach (var d in SettingsSchema.For(citizenType))
        {
            if (!lookup.TryGetValue(d.Name, out var raw) || raw is null) continue;
            JsonNode? node = d.ValueKind switch
            {
                SettingValueKind.Bool => bool.TryParse(raw, out var b) ? JsonValue.Create(b) : null,
                SettingValueKind.Integer => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? JsonValue.Create(l) : null,
                SettingValueKind.Number => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? JsonValue.Create(n) : null,
                SettingValueKind.Choice => d.Choices.FirstOrDefault(c => string.Equals(c, raw, StringComparison.OrdinalIgnoreCase)) is { } c ? JsonValue.Create(c) : null,
                _ => JsonValue.Create(raw),
            };
            if (node is not null) obj[d.Name] = node;
        }
        return obj.ToJsonString();
    }

    /// <summary>Stored JSON → Blazor parameters for the citizen (only its declared settings, coerced to type).</summary>
    public static Dictionary<string, object?> ToParameters(Type citizenType, string? json)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;
        foreach (var (name, raw) in Parse(json))
        {
            var d = SettingsSchema.Find(citizenType, name);
            if (d is null || raw is null) continue;
            var value = IncludeExpander.CoerceAttributeValue(d.ClrType, raw);
            var target = Nullable.GetUnderlyingType(d.ClrType) ?? d.ClrType;
            if (value is not null && target.IsInstanceOfType(value)) result[d.Name] = value;
        }
        return result;
    }
}
