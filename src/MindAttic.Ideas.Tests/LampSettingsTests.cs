using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using MindAttic.Ideas.Blazor.Services;
using MindAttic.Vault.Paths;
using MindAttic.Vault.Settings;
using MindAttic.Vault.Settings.Manifest;
using NUnit.Framework;

namespace MindAttic.Ideas.Tests;

/// <summary>Every test in this assembly sees temp Vault roots, so nothing can write the real AppData.</summary>
[SetUpFixture]
public sealed class TestVaultRoots
{
    private string root = "";

    [OneTimeSetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "IdeasVault-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, Path.Combine(root, "roaming"));
        Environment.SetEnvironmentVariable(VaultPaths.LocalRootEnvVar, Path.Combine(root, "local"));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(VaultPaths.RoamingRootEnvVar, null);
        Environment.SetEnvironmentVariable(VaultPaths.LocalRootEnvVar, null);
        try { Directory.Delete(root, recursive: true); } catch { }
    }
}

/// <summary>LAMP (M.IDEA-T29): Ideas' app-level settings in Vault, projected onto the keys Ideas reads; one case per setting.</summary>
[NonParallelizable]
public class LampSettingsTests
{
    private string dir = "";
    private JsonSettingsStore<IdeasSettings> Store() => new(dir);

    [SetUp]
    public void SetUp() => dir = Path.Combine(Path.GetTempPath(), "ideas-lamp-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static readonly SettingsManifest Manifest = SettingsManifestBuilder.Build<IdeasSettings>();

    /// <summary>Every leaf setting (not the group objects), by its JSON path under the app.</summary>
    public static IEnumerable<TestCaseData> Leaves() =>
        Manifest.AllFields()
            .Where(f => f.Kind is not SettingKind.Object and not SettingKind.Collection)
            .Select(f => new TestCaseData(f.Name[(Manifest.ConfigurationPrefix.Length + 1)..], f.Kind.ToString())
                .SetName("Setting_" + f.Name[(Manifest.ConfigurationPrefix.Length + 1)..].Replace(':', '_') + "_RoundTripsAndReachesItsConfigurationKey"));

    /// <summary>The configuration key Ideas has always read for a setting path.</summary>
    private static string KeyFor(string path)
    {
        var parts = path.Split(':');
        return parts[0] switch
        {
            "packages" when parts[1] == "packagesDirs" => "Ideas:PackagesDirs:0",
            "packages" => $"Ideas:Packages:{parts[1]}",
            "media" when parts[1] == "provider" => "Media:Provider",
            "media" => $"Media:Azure:{parts[1]}",
            "auth" => $"MindAttic:Auth:{string.Join(':', parts[1..])}",
            _ => throw new ArgumentException(path),
        };
    }

    private static (JsonNode Value, string Expected) Distinct(SettingField field) => field.Kind switch
    {
        SettingKind.Bool => (JsonValue.Create(!(field.Default?.GetValue<bool>() ?? false))!, (!(field.Default?.GetValue<bool>() ?? false)).ToString().ToLowerInvariant()),
        SettingKind.Integer => (JsonValue.Create(field.Name.Contains("signedUrlMinutes") ? 77 : 4242)!, field.Name.Contains("signedUrlMinutes") ? "77" : "4242"),
        SettingKind.Decimal => (JsonValue.Create(4.5)!, "4.5"),
        SettingKind.TimeSpan => (JsonValue.Create("01:23:45")!, "01:23:45"),
        SettingKind.StringList => (new JsonArray(JsonValue.Create(@"D:\pkgs")), @"D:\pkgs"),
        SettingKind.Choice => (field.Choices!.Last().Value!.DeepClone(), field.Choices!.Last().Value!.ToString()),
        SettingKind.Url => (JsonValue.Create("https://example.test")!, "https://example.test"),
        _ => (JsonValue.Create("value-" + field.Property)!, "value-" + field.Property),
    };

    [TestCaseSource(nameof(Leaves))]
    public void EverySetting_RoundTripsThroughVaultAndReachesTheKeyIdeasReads(string path, string kind)
    {
        var field = Manifest.Find($"{Manifest.ConfigurationPrefix}:{path}")!;
        var (value, expected) = Distinct(field);
        var store = Store();
        store.LoadOrSeed();
        var doc = JsonNode.Parse(File.ReadAllText(store.FilePath))!;
        var parts = path.Split(':');
        var node = doc;
        foreach (var p in parts[..^1]) node = node[p]!;
        node[parts[^1]] = value;
        File.WriteAllText(store.FilePath, doc.ToJsonString());

        var keys = IdeasSettings.Load(Config(new()), Store()).ToConfigurationKeys();
        var key = KeyFor(path);
        Assert.That(keys.TryGetValue(key, out var got), Is.True, key);
        Assert.That(got, Is.EqualTo(expected).IgnoreCase, key);
    }

    [Test]
    public void TodaysAppsettingsValuesAreImportedOnce()
    {
        var s = IdeasSettings.Load(Config(new()
        {
            ["Ideas:Packages:OverwriteVersionConflicts"] = "false",
            ["Media:Provider"] = "azure",
            ["Media:Azure:ContainerName"] = "assets",
            ["Media:Azure:SignedUrlMinutes"] = "15",
            ["MindAttic:Auth:Mfa:RequireForAdmin"] = "true",
            ["MindAttic:Auth:Reset:PublicBaseUrl"] = "https://localhost:7207",
        }), Store());
        Assert.That((s.Packages.OverwriteVersionConflicts, s.Media.Provider, s.Media.ContainerName, s.Media.SignedUrlMinutes),
            Is.EqualTo((false, "azure", "assets", 15)));
        Assert.That((s.Auth.Mfa.RequireForAdmin, s.Auth.Reset.PublicBaseUrl), Is.EqualTo((true, "https://localhost:7207")));
        var again = IdeasSettings.Load(Config(new() { ["Media:Provider"] = "local" }), Store());
        Assert.That(again.Media.Provider, Is.EqualTo("azure"), "the Vault file wins once it exists");
    }

    [Test]
    public void FreshInstall_KeepsTodaysEffectiveDefaults()
    {
        var s = IdeasSettings.Load(Config(new()), Store());
        Assert.That(s.Packages.OverwriteVersionConflicts, Is.True, "appsettings.json shipped true");
        Assert.That(s.Auth.Mfa.RequireForAdmin, Is.False, "MFA for admins is off for now (M.IDEA-§4.3)");
        Assert.That((s.Media.Provider, s.Media.ContainerName, s.Media.SignedUrlMinutes), Is.EqualTo(("local", "media", 60)));
    }

    [Test]
    public void EnvironmentVariablesStillWinOverTheProjection()
    {
        var builder = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream("{}"u8.ToArray()))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Media:Provider"] = "azure" });
        IdeasSettings.Load(Config(new()), Store()).AddTo(builder);
        var config = builder.Build();
        Assert.That(config["Media:Provider"], Is.EqualTo("azure"), "a later source (App Service setting) beats the Vault projection");
        Assert.That(config["Media:Azure:ContainerName"], Is.EqualTo("media"), "Vault supplies what nothing later overrides");
    }

    [Test]
    public void DeployedAppConfigurationValuesWin()
    {
        Store().Save(new IdeasSettings());
        var s = IdeasSettings.Load(Config(new() { ["MindAttic:Settings:Ideas:media:provider"] = "azure" }), Store());
        Assert.That(s.Media.Provider, Is.EqualTo("azure"));
    }

    [Test]
    public void ManifestCoversEveryAppLevelSettingAndNoSecrets()
    {
        var names = Manifest.AllFields().Select(f => f.Name).ToList();
        Assert.That(names, Does.Contain("MindAttic:Settings:Ideas:media:blobServiceUri").And.Not.Contain("MindAttic:Settings:Ideas:media:connectionString"));
        Assert.That(Leaves().Count(), Is.GreaterThan(35), "packages + media + every auth option");
    }

    [Test]
    public void RunScript_WritesTheSettingsManifestFromTheDeployedExe()
    {
        // LAMP step 7 (M.IDEA-T33): after publishing, run.ps1 runs the deployed exe once with --write-settings-manifest
        // (Development, like serve.bat), so Vault.Desktop's Ideas tab matches the build just deployed.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MindAttic.Ideas.slnx"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "repo root");
        var script = File.ReadAllText(Path.Combine(dir!.FullName, "run.ps1"));

        var manifest = script.IndexOf("& $exe " + SettingsManifestCommand.Switch, StringComparison.Ordinal);
        Assert.That(manifest, Is.GreaterThan(0), "run.ps1 runs the deployed exe with " + SettingsManifestCommand.Switch);
        Assert.That(manifest, Is.GreaterThan(script.IndexOf("dotnet publish", StringComparison.Ordinal)), "after publishing");
        Assert.That(manifest, Is.LessThan(script.IndexOf("if ($Launch)", StringComparison.Ordinal)), "before launching");
        var block = script[script.LastIndexOf("if (Test-Path $exe)", manifest, StringComparison.Ordinal)..manifest];
        Assert.That(block, Does.Contain("ASPNETCORE_ENVIRONMENT = 'Development'"), "with the deployed copy's environment");
    }
}
