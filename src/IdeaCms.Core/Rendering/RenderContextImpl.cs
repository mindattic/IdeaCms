using System.Text.Json;
using IdeaCms.Abstractions;

namespace IdeaCms.Core.Rendering;

/// <summary>Host serializer options for settings (pinned; wire format treated as additive-only).</summary>
public static class CmsJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>Concrete <see cref="IRenderContext"/> the host builds per render.</summary>
public sealed class CmsRenderContext : IRenderContext
{
    public required Guid InstanceId { get; init; }
    public required ContentMode Mode { get; init; }
    public required CmsRenderMode RenderMode { get; init; }
    public required IPageContext Page { get; init; }
    public required ISiteContext Site { get; init; }
    public required IServiceProvider Services { get; init; }
    public string? RawSettingsJson { get; init; }

    /// <summary>This page's stored instance-settings slots (slot name → JSON), M.IDEA-§4.5.</summary>
    public IReadOnlyDictionary<string, string> InstanceSettings { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string? GetInstanceSettingsJson(string slot) => InstanceSettings.GetValueOrDefault(slot);

    public T GetSettings<T>() where T : class, new() =>
        string.IsNullOrWhiteSpace(RawSettingsJson)
            ? new T()
            : JsonSerializer.Deserialize<T>(RawSettingsJson, CmsJson.Options) ?? new T();
}

public sealed class CmsPageContext : IPageContext
{
    public required Guid PageId { get; init; }
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public string? SeoTitle { get; init; }
    public string? ThemeKey { get; init; }
    public int? ThemeVersion { get; init; }
    public required IInlineMarkup Inline { get; init; }
    public IReadOnlyDictionary<string, string?> Meta { get; init; } = new Dictionary<string, string?>();
}

public sealed class CmsSiteContext : ISiteContext
{
    public required Guid SiteId { get; init; }
    public required string Key { get; init; }
    public required string Host { get; init; }
    public required string DefaultThemeKey { get; init; }
    public Func<string, string?>? SettingReader { get; init; }
    public string? GetSetting(string key) => SettingReader?.Invoke(key);
}

public sealed class CmsInlineMarkup : IInlineMarkup
{
    public string? Html { get; init; }
    public string? Css { get; init; }
    public string? Js { get; init; }
    public bool Trusted { get; init; }
}
