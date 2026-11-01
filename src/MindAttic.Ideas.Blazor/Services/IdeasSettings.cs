using System.Text.Json;
using Microsoft.Extensions.Configuration.Memory;
using MindAttic.Authentication.Options;
using MindAttic.Vault.Settings;
using MindAttic.Vault.Settings.Manifest;

namespace MindAttic.Ideas.Blazor.Services;

/// <summary>
/// Ideas' app-level settings, edited in MindAttic.Vault.Desktop: locally <c>%APPDATA%\MindAttic\Ideas\settings.json</c>,
/// deployed <c>MindAttic:Settings:Ideas:*</c> in App Configuration. Site, page and widget settings are CMS content and stay
/// in the admin. The values are projected onto the configuration keys the app has always read (<see cref="ToConfigurationKeys"/>),
/// below environment variables, so an App Service setting still wins exactly as before.
/// </summary>
[SettingsApp(AppId, "MindAttic.Ideas", Icon = "lightbulb", Description = "The CMS behind mindattic.com and its sites.")]
public sealed class IdeasSettings
{
    public const string AppId = "Ideas";

    [Setting("Packages", Order = 1)]
    public PackageSettings Packages { get; set; } = new();

    [Setting("Media storage", Order = 2)]
    public MediaSettings Media { get; set; } = new();

    [Setting("Sign-in security", Order = 3)]
    public MindAtticAuthSettings Auth { get; set; } = new() { Mfa = { RequireForAdmin = false } };

    public sealed class PackageSettings
    {
        [Setting("Overwrite a changed package at the same version", Group = "Packages", Order = 1,
            Help = "Pre-lockdown iteration: a changed .idea at an already-installed version replaces it instead of being refused. Turn off to lock down.")]
        public bool OverwriteVersionConflicts { get; set; } = true;

        [Setting("Package storage folder", Group = "Packages", Order = 2, Help = "Where installed packages are kept. Empty uses the app's default.")]
        [PathPicker(PathKind.Folder)]
        public string? StorageRoot { get; set; }

        [Setting("Extra package folders", Group = "Packages", Order = 3, Help = "Folders of .idea packages provisioned at boot.")]
        public List<string> PackagesDirs { get; set; } = [];
    }

    public sealed class MediaSettings
    {
        [Setting("Provider", Group = "Media storage", Order = 1, Help = "local keeps media on disk; azure streams it to Blob Storage (credentials in Vault's Media credentials).")]
        [SettingChoices("local", "azure")]
        public string Provider { get; set; } = MediaProviderSetup.DefaultProvider;

        [Setting("Blob container", Group = "Media storage", Order = 2)]
        public string ContainerName { get; set; } = "media";

        [Setting("Signed URL lifetime (minutes)", Group = "Media storage", Order = 3)]
        [SettingRange(1, 10080)]
        public int SignedUrlMinutes { get; set; } = 60;

        [Setting("Public read container", Group = "Media storage", Order = 4, Help = "On when the container allows anonymous reads: plain, CDN-cacheable URLs instead of a SAS.")]
        public bool PublicRead { get; set; }

        [Setting("Blob service URI", Group = "Media storage", Order = 5, Help = "https://<account>.blob.core.windows.net, signed in with DefaultAzureCredential. Empty when a connection string is used.")]
        [UrlSetting]
        public string? BlobServiceUri { get; set; }
    }

    /// <summary>
    /// The settings: deployed App Configuration values when present; otherwise the Vault file, importing today's
    /// <c>Ideas:*</c>, <c>Media:*</c> and <c>MindAttic:Auth:*</c> configuration once.
    /// </summary>
    public static IdeasSettings Load(IConfiguration configuration, ISettingsStore<IdeasSettings>? store = null)
    {
        var deployed = configuration.GetSection(SettingsManifest.PrefixFor(AppId));
        if (deployed.Exists()) return deployed.Get<IdeasSettings>() ?? new IdeasSettings();

        store ??= JsonSettingsStore<IdeasSettings>.ForSettingsApp();
        if (!store.Exists())
        {
            var s = new IdeasSettings();
            if (configuration["Ideas:Packages:OverwriteVersionConflicts"] is { } ow && bool.TryParse(ow, out var owv)) s.Packages.OverwriteVersionConflicts = owv;
            if (configuration["Ideas:Packages:StorageRoot"] is { Length: > 0 } sr) s.Packages.StorageRoot = sr;
            if (configuration.GetSection("Ideas:PackagesDirs").Get<string[]>() is { Length: > 0 } dirs) s.Packages.PackagesDirs = [.. dirs];
            configuration.GetSection("Media").Bind(s.Media);
            configuration.GetSection("Media:Azure").Bind(s.Media);
            if (configuration.GetSection(MindAtticAuthSettings.LegacySection).Exists())
            {
                var auth = MindAtticAuthSettings.FromConfiguration(configuration);
                if (configuration[$"{MindAtticAuthSettings.LegacySection}:Mfa:RequireForAdmin"] is null)
                    auth.Mfa.RequireForAdmin = s.Auth.Mfa.RequireForAdmin;
                s.Auth = auth;
            }
            store.Save(s);
        }
        return store.LoadOrSeed();
    }

    /// <summary>The configuration keys Ideas reads, with these values.</summary>
    public IReadOnlyDictionary<string, string?> ToConfigurationKeys()
    {
        var keys = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ideas:Packages:OverwriteVersionConflicts"] = Packages.OverwriteVersionConflicts ? "true" : "false",
            ["Media:Provider"] = Media.Provider,
            ["Media:Azure:ContainerName"] = Media.ContainerName,
            ["Media:Azure:SignedUrlMinutes"] = Media.SignedUrlMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Media:Azure:PublicRead"] = Media.PublicRead ? "true" : "false",
        };
        if (!string.IsNullOrWhiteSpace(Packages.StorageRoot)) keys["Ideas:Packages:StorageRoot"] = Packages.StorageRoot;
        for (var i = 0; i < Packages.PackagesDirs.Count; i++) keys[$"Ideas:PackagesDirs:{i}"] = Packages.PackagesDirs[i];
        if (!string.IsNullOrWhiteSpace(Media.BlobServiceUri)) keys["Media:Azure:BlobServiceUri"] = Media.BlobServiceUri;
        Flatten(keys, MindAtticAuthSettings.LegacySection, JsonSerializer.SerializeToElement(Auth));
        return keys;
    }

    /// <summary>Adds the projection just above the JSON files, so environment variables and later sources still win.</summary>
    public void AddTo(IConfigurationBuilder configuration)
    {
        var source = new MemoryConfigurationSource { InitialData = ToConfigurationKeys() };
        var sources = configuration.Sources;
        var lastJson = -1;
        for (var i = 0; i < sources.Count; i++)
            if (sources[i] is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource) lastJson = i;
        sources.Insert(lastJson + 1, source);
    }

    private static void Flatten(IDictionary<string, string?> keys, string path, JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject()) Flatten(keys, $"{path}:{p.Name}", p.Value);
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in e.EnumerateArray()) Flatten(keys, $"{path}:{i++}", item);
                break;
            case JsonValueKind.Null:
                break;
            // An unset text setting must not blank a lower source (the dev-only reset URL in appsettings.Development.json).
            case JsonValueKind.String when string.IsNullOrEmpty(e.GetString()):
                break;
            default:
                keys[path] = e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText();
                break;
        }
    }
}
