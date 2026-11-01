using IdeaCms.Packaging;

namespace IdeaCms.Tests.Packaging;

[TestFixture]
public class ManifestValidatorTests
{
    private static IdeaManifest Code(string key = "ui.tooltip", int version = 1, string category = "Plugin",
        int? sdk = 1, string? entry = "IdeaCms.Plugin.Demo.V1") =>
        new() { ManifestVersion = 1, Category = category, Kind = "code", Key = key, Version = version,
                DisplayName = "Demo", Sdk = sdk, EntryType = entry };

    private static IdeaManifest Data(string key = "about", int version = 1) =>
        new() { ManifestVersion = 1, Category = "Page", Kind = "data", Key = key, Version = version, DisplayName = "About" };

    private static bool HasError(ValidationResult r, string code) => r.HardErrors.Any(e => e.Code == code);

    [Test]
    public void ManifestTooNew_ErrorMentionsUpgrade()
    {
        var m = Code() with { ManifestVersion = IdeaManifest.HostMaxManifestVersion + 1 };
        var r = ManifestValidator.Validate(m, ["Demo.dll"]);
        Assert.That(r.IsValid, Is.False);
        var err = r.HardErrors.Single(e => e.Code == ManifestValidator.ManifestTooNew);
        Assert.That(err.Message, Does.Contain("upgrade"));
    }

    [Test]
    public void UnknownKind_IsError_UnknownCategory_IsWarningOnly()
    {
        var badKind = ManifestValidator.Validate(Code() with { Kind = "widget" }, ["Demo.dll"]);
        Assert.That(HasError(badKind, ManifestValidator.BadKind), Is.True);

        var badCat = ManifestValidator.Validate(Code() with { Category = "Sidebar" }, ["Demo.dll"]);
        Assert.Multiple(() =>
        {
            Assert.That(badCat.IsValid, Is.True, "unknown category is a warning, not a hard error");
            Assert.That(badCat.Errors.Any(e => e.Code == ManifestValidator.UnknownCategory && e.IsWarning), Is.True);
        });
    }

    [TestCase("Widget")]
    [TestCase("widget")]
    [TestCase("Control")]
    [TestCase("control")]
    public void RetiredCategory_IsHardError(string retiredCategory)
    {
        var r = ManifestValidator.Validate(Code() with { Category = retiredCategory }, ["Demo.dll"]);
        Assert.Multiple(() =>
        {
            Assert.That(r.IsValid, Is.False, "retired category must be a hard error");
            Assert.That(HasError(r, ManifestValidator.RetiredCategory), Is.True);
        });
    }

    [TestCase("about", true)]
    [TestCase("ui.tooltip", true)]
    [TestCase("a0-_", true)]
    [TestCase("", false)]
    [TestCase(".leading", false)]
    [TestCase("Upper", false)]
    [TestCase("has space", false)]
    public void KeyRegex_AcceptsValid_RejectsInvalid(string key, bool valid)
    {
        var r = ManifestValidator.Validate(Code(key: key), ["Demo.dll"]);
        Assert.That(HasError(r, ManifestValidator.BadKey), Is.EqualTo(!valid));
    }

    [Test]
    public void KeyTooLong_IsRejected()
    {
        var r = ManifestValidator.Validate(Code(key: new string('a', 121)), ["Demo.dll"]);
        Assert.That(HasError(r, ManifestValidator.BadKey), Is.True);
    }

    [Test]
    public void VersionBelowOne_IsError()
    {
        var r = ManifestValidator.Validate(Code(version: 0), ["Demo.dll"]);
        Assert.That(HasError(r, ManifestValidator.BadVersion), Is.True);
    }

    [Test]
    public void Code_WithoutEntry_WithoutBin_WithFutureSdk_AllError()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HasError(ManifestValidator.Validate(Code(entry: null), ["Demo.dll"]), ManifestValidator.CodeMissingEntry), Is.True);
            Assert.That(HasError(ManifestValidator.Validate(Code(), []), ManifestValidator.CodeMissingBin), Is.True);
            Assert.That(HasError(ManifestValidator.Validate(Code(sdk: IdeaManifest.HostSdkVersion + 1), ["Demo.dll"]), ManifestValidator.SdkTooNew), Is.True);
        });
    }

    [Test]
    public void Code_Valid_WhenSdkAtHostMax()
    {
        var r = ManifestValidator.Validate(Code(sdk: IdeaManifest.HostSdkVersion), ["Demo.dll"]);
        Assert.That(r.IsValid, Is.True, r.Summary);
    }

    [Test]
    public void Data_CarryingEntryOrBin_IsError()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HasError(ManifestValidator.Validate(Data() with { EntryType = "X" }, []), ManifestValidator.DataHasCode), Is.True);
            Assert.That(HasError(ManifestValidator.Validate(Data(), ["Stowaway.dll"]), ManifestValidator.DataHasCode), Is.True);
        });
        Assert.That(ManifestValidator.Validate(Data(), []).IsValid, Is.True, "a clean data package is valid");
    }

    [TestCase("MindAttic.Ideas.Abstractions.dll")]
    [TestCase("IdeaCms.Core.dll")]
    [TestCase("Microsoft.AspNetCore.Components.dll")]
    [TestCase("Microsoft.Extensions.DependencyInjection.dll")]
    [TestCase("Microsoft.EntityFrameworkCore.dll")]
    [TestCase("Microsoft.JSInterop.dll")]
    [TestCase("System.Text.Json.dll")]
    [TestCase("netstandard.dll")]
    [TestCase("mscorlib.dll")]
    public void ForbiddenHostAssemblyInBin_IsError(string bin)
    {
        var r = ManifestValidator.Validate(Code(), [bin]);
        Assert.That(HasError(r, ManifestValidator.ForbiddenBin), Is.True);
    }

    [Test]
    public void LegitPrivateDependencyInBin_IsAllowed()
    {
        var r = ManifestValidator.Validate(Code(), ["Demo.dll", "Markdig.dll"]);
        Assert.That(r.IsValid, Is.True, r.Summary);
    }

    [Test]
    public void ShaMismatch_IsError()
    {
        var r = ManifestValidator.Validate(Code(), ["Demo.dll"], expectedSha: "aaaa", actualSha: "bbbb");
        Assert.That(HasError(r, ManifestValidator.ShaMismatch), Is.True);
    }

    [TestCase(null,  true)]
    [TestCase(0,     true)]
    [TestCase(1,     true)]   // == HostEngineVersion
    public void MinHostVersion_AbsentOrAtHost_IsValid(int? minHostVersion, bool expectedValid)
    {
        var m = Code() with { MinHostVersion = minHostVersion };
        var r = ManifestValidator.Validate(m, ["Demo.dll"]);
        Assert.That(HasError(r, ManifestValidator.MinHostVersionUnmet), Is.EqualTo(!expectedValid));
    }

    [Test]
    public void MinHostVersion_ExceedsHostEngine_IsHardError()
    {
        var m = Code() with { MinHostVersion = IdeaManifest.HostEngineVersion + 1 };
        var r = ManifestValidator.Validate(m, ["Demo.dll"]);
        Assert.Multiple(() =>
        {
            Assert.That(r.IsValid, Is.False);
            Assert.That(HasError(r, ManifestValidator.MinHostVersionUnmet), Is.True);
            Assert.That(r.HardErrors.Single(e => e.Code == ManifestValidator.MinHostVersionUnmet).Message,
                Does.Contain("upgrade"));
        });
    }
}
