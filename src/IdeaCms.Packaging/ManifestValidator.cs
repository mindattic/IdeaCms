using System.Text.RegularExpressions;
using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Packaging;

/// <summary>A single validation failure. <see cref="IsWarning"/> failures do not invalidate a package.</summary>
public readonly record struct ValidationError(string Code, string Message, bool IsWarning = false);

/// <summary>The outcome of <see cref="ManifestValidator.Validate"/>. Valid when there are no hard errors.</summary>
public sealed record ValidationResult(IReadOnlyList<ValidationError> Errors)
{
    public bool IsValid => Errors.All(e => e.IsWarning);
    public IEnumerable<ValidationError> HardErrors => Errors.Where(e => !e.IsWarning);
    public string Summary => string.Join("; ", Errors.Select(e => $"{e.Code}: {e.Message}"));
}

/// <summary>
/// Validates a parsed <see cref="IdeaManifest"/> against the ADR rule set. PURE — the bin/ entry list is
/// passed in (from <see cref="IdeaArchiveReader.BinEntries"/>) so this needs no archive. NEVER throws for
/// a bad package; every problem is reported as a <see cref="ValidationError"/>. This is also where the
/// ALC unification audit (no host/framework assembly may ship in bin/) is enforced — at validate time, so
/// a bad package is rejected before any assembly is ever loaded.
/// </summary>
public static partial class ManifestValidator
{
    public const string ManifestTooNew = "MANIFEST_TOO_NEW";
    public const string BadKey = "BAD_KEY";
    public const string BadVersion = "BAD_VERSION";
    public const string NoDisplayName = "NO_DISPLAY_NAME";
    public const string BadKind = "BAD_KIND";
    public const string UnknownCategory = "UNKNOWN_CATEGORY";
    public const string RetiredCategory = "RETIRED_CATEGORY";
    public const string CodeMissingEntry = "CODE_MISSING_ENTRY";
    public const string CodeMissingBin = "CODE_MISSING_BIN";
    public const string SdkTooNew = "SDK_TOO_NEW";
    public const string DataHasCode = "DATA_HAS_CODE";
    public const string ForbiddenBin = "FORBIDDEN_BIN";
    public const string ShaMismatch = "SHA_MISMATCH";
    public const string MinHostVersionUnmet = "MIN_HOST_VERSION_UNMET";

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]{0,119}$")] private static partial Regex KeyPattern();

    public static ValidationResult Validate(
        IdeaManifest m,
        IReadOnlyList<string> binEntries,
        int hostSdk = IdeaManifest.HostSdkVersion,
        int hostEngine = IdeaManifest.HostEngineVersion,
        string? expectedSha = null,
        string? actualSha = null)
    {
        var errors = new List<ValidationError>();

        if (m.ManifestVersion > IdeaManifest.HostMaxManifestVersion)
            errors.Add(new(ManifestTooNew,
                $"manifestVersion {m.ManifestVersion} is newer than this host supports " +
                $"({IdeaManifest.HostMaxManifestVersion}); upgrade IdeaCms to install it."));

        if (!KeyPattern().IsMatch(m.Key))
            errors.Add(new(BadKey, $"key '{m.Key}' must match ^[a-z0-9][a-z0-9._-]{{0,119}}$ (lowercase, no spaces)."));

        if (m.Version < 1)
            errors.Add(new(BadVersion, $"version must be a whole number ≥ 1 (was {m.Version})."));

        if (string.IsNullOrWhiteSpace(m.DisplayName))
            errors.Add(new(NoDisplayName, "displayName is required."));

        var isData = string.Equals(m.Kind, "data", StringComparison.Ordinal);
        var isCode = string.Equals(m.Kind, "code", StringComparison.Ordinal);
        if (!isData && !isCode)
            errors.Add(new(BadKind, $"kind '{m.Kind}' must be 'data' or 'code'."));

        // Categories outside M.IDEA-LAW-1's four kinds (e.g. Widget, Control) are a hard error — they parse to wrong ordinals at runtime.
        if (string.Equals(m.Category, "Widget", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.Category, "Control", StringComparison.OrdinalIgnoreCase))
            errors.Add(new(RetiredCategory, $"category '{m.Category}' was retired — use Plugin or Component instead."));
        // Unknown-but-not-retired category is a WARNING — a future ContentKind shouldn't hard-block install.
        else if (!Enum.TryParse<ContentKind>(m.Category, ignoreCase: true, out _))
            errors.Add(new(UnknownCategory, $"category '{m.Category}' is not a known ContentKind.", IsWarning: true));

        if (isCode)
        {
            if (string.IsNullOrWhiteSpace(m.EntryType))
                errors.Add(new(CodeMissingEntry, "a code package must declare entryType."));
            if (binEntries.Count == 0)
                errors.Add(new(CodeMissingBin, "a code package must ship at least one assembly in bin/."));
            if (m.Sdk is null)
                errors.Add(new(SdkTooNew, "a code package must declare the sdk it was built against."));
            else if (m.Sdk > hostSdk)
                errors.Add(new(SdkTooNew,
                    $"package sdk {m.Sdk} is newer than this host provides ({hostSdk}); upgrade IdeaCms."));
        }

        if (isData)
        {
            if (!string.IsNullOrWhiteSpace(m.EntryType))
                errors.Add(new(DataHasCode, "a data package must not declare entryType."));
            if (binEntries.Count > 0)
                errors.Add(new(DataHasCode, "a data package must not ship assemblies in bin/."));
        }

        // The ALC unification audit: a package may never ship a host/framework assembly — its base types
        // must unify by reference identity with the host's. Enforced here so a bad package never reaches B.
        foreach (var entry in binEntries)
        {
            if (IsHostAssemblyName(Path.GetFileNameWithoutExtension(entry)))
                errors.Add(new(ForbiddenBin,
                    $"bin/ ships host/framework assembly '{entry}', which must be host-provided (it cannot ship in a .idea)."));
        }

        if (m.MinHostVersion is int minHost && minHost > hostEngine)
            errors.Add(new(MinHostVersionUnmet,
                $"package requires host engine version ≥ {minHost} (this host is v{hostEngine}); upgrade IdeaCms to install it."));

        if (expectedSha is not null && actualSha is not null &&
            !string.Equals(expectedSha, actualSha, StringComparison.OrdinalIgnoreCase))
            errors.Add(new(ShaMismatch, "package SHA-256 does not match the expected hash."));

        return new ValidationResult(errors);
    }

    /// <summary>
    /// True if a bundled assembly's simple name is host-provided/framework and must NOT ship in a .idea.
    /// Matches a <see cref="SharedContracts.DeferToDefaultPrefixes"/> entry either exactly (so the bare
    /// <c>Microsoft.EntityFrameworkCore</c> is caught, not just its sub-assemblies) or as a dotted prefix
    /// (so <c>System.Text.Json</c> matches <c>System.</c> while a legit <c>Systematic</c> does not).
    /// </summary>
    public static bool IsHostAssemblyName(string simpleName)
    {
        foreach (var p in SharedContracts.DeferToDefaultPrefixes)
        {
            var stem = p.TrimEnd('.');
            if (simpleName.Equals(stem, StringComparison.OrdinalIgnoreCase) ||
                simpleName.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
