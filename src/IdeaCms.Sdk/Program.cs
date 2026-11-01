using System.Security.Cryptography.X509Certificates;
using IdeaCms.Packaging;
using NuGet.Packaging;
using NuGet.Versioning;

// ma-idea — the .idea CLI.
//
//   ma-idea pack    --assembly <built.dll> --wwwroot <dir> --out <dir> [--icon <file>] [--version <n>] [--refs <a;b>]
//   ma-idea inspect <file.idea>
//   ma-idea list    <dir>
//   ma-idea install <file.idea> [--allow-override]      (OFFLINE validate-only; the host applies installs)
//   ma-idea upgrade <file.idea>                          (validate + plan against the .idea files beside it)
//   ma-idea sign    <file.idea> --pfx <cert.pfx> [--password <pw>]      (content-signs in place)
//   ma-idea nupkg   --idea <file.idea> --out <dir>                     (packs a signed .idea into a .nupkg)
//
// pack reads the entry type's identity (Kind, Key, Version) by CONVENTION from its namespace and Vn class
// name (reflection-only; never executes the assembly). The read verbs (inspect/list/install/upgrade) are
// pure and offline — they never touch a database. Installing a package into a running site is a host
// operation (PackageInstallService); disabling likewise. All logic lives in IdeaCms.Packaging.
//
// sign/nupkg are the publish-side half of NuGet distribution (M.IDEA-§4.8): sign stamps a .idea's
// OWN content signature (PackageSigner — independent of NuGet's own signing feature, verified by every
// install path); nupkg wraps an already-signed .idea into a `IdeaCms.{Category}.{Key}` package
// at version `{n}.0.0`, ready for `dotnet nuget push` against a feed (see library/tools/publish-nuget.ps1).

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintHelp();
    return 0;
}

var verb = args[0].ToLowerInvariant();
var rest = args.AsSpan(1);

try
{
    return verb switch
    {
        "pack" => RunPack(rest),
        "inspect" => RunInspect(rest),
        "list" => RunList(rest),
        "verify" => RunVerify(rest),
        "install" => RunInstall(rest),
        "upgrade" => RunUpgrade(rest),
        "disable" => RunDisable(),
        "sign" => RunSign(rest),
        "nupkg" => RunNupkg(rest),
        _ => Unknown(verb),
    };
}
catch (PackException ex)
{
    Console.Error.WriteLine($"ma-idea {verb}: {ex.Message}");
    return 1;
}

static int Unknown(string verb)
{
    Console.Error.WriteLine($"ma-idea: unknown command '{verb}'. Try 'ma-idea --help'.");
    return 2;
}

static int RunPack(ReadOnlySpan<string> rest)
{
    var opts = ArgParser.Parse(rest);
    string? assembly = opts.GetValueOrDefault("assembly");
    string? outDir = opts.GetValueOrDefault("out");
    string? wwwroot = opts.GetValueOrDefault("wwwroot");
    string? data = opts.GetValueOrDefault("data");
    string? icon = opts.GetValueOrDefault("icon");
    int? versionOverride = opts.TryGetValue("version", out var v) && int.TryParse(v, out var vn) ? vn : null;
    var refs = (opts.GetValueOrDefault("refs") ?? "")
        .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();

    if (string.IsNullOrWhiteSpace(assembly) || string.IsNullOrWhiteSpace(outDir))
    {
        Console.Error.WriteLine("ma-idea pack: --assembly and --out are required. Try 'ma-idea --help'.");
        return 2;
    }
    if (!File.Exists(assembly))
    {
        Console.Error.WriteLine($"ma-idea pack: assembly not found: {assembly}");
        return 2;
    }

    var path = Packer.Pack(new PackRequest
    {
        AssemblyPath = Path.GetFullPath(assembly),
        WwwrootDir = string.IsNullOrWhiteSpace(wwwroot) ? null : Path.GetFullPath(wwwroot),
        DataDir = string.IsNullOrWhiteSpace(data) ? null : Path.GetFullPath(data),
        OutputDir = Path.GetFullPath(outDir),
        IconPath = string.IsNullOrWhiteSpace(icon) ? null : Path.GetFullPath(icon),
        VersionOverride = versionOverride,
        ReferenceInputs = refs,
    });
    Console.WriteLine($"ma-idea: packed {Path.GetFileName(path)}");
    return 0;
}

static int RunInspect(ReadOnlySpan<string> rest)
{
    if (!TryFile(rest, "inspect", out var file)) return 2;
    using var reader = IdeaArchiveReader.Open(file);
    if (!reader.TryReadManifest(out var m, out var err)) { Console.Error.WriteLine($"ma-idea inspect: {err}"); return 1; }
    Console.WriteLine($"key          {m!.Key}");
    Console.WriteLine($"category     {m.Category}");
    Console.WriteLine($"kind         {m.Kind}");
    Console.WriteLine($"version      {m.Version}");
    Console.WriteLine($"displayName  {m.DisplayName}");
    if (m.EntryType is not null) Console.WriteLine($"entryType    {m.EntryType}");
    if (m.Uses.Count > 0) Console.WriteLine($"uses         {string.Join(", ", m.Uses)}");
    Console.WriteLine($"bin/         {reader.BinEntries().Count} file(s)");
    Console.WriteLine($"wwwroot/     {reader.WwwrootEntries().Count} file(s)");
    Console.WriteLine($"data/        {reader.DataEntries().Count} file(s)");
    return 0;
}

static int RunList(ReadOnlySpan<string> rest)
{
    if (!TryDir(rest, "list", out var dir)) return 2;
    var files = Directory.EnumerateFiles(dir, "*.idea").OrderBy(f => f, StringComparer.Ordinal).ToList();
    if (files.Count == 0) { Console.WriteLine("(no .idea packages found)"); return 0; }
    foreach (var f in files)
    {
        using var reader = IdeaArchiveReader.Open(f);
        if (reader.TryReadManifest(out var m, out _))
            Console.WriteLine($"{m!.Key,-28} v{m.Version,-4} {m.Category,-10} {m.Kind,-5} {m.DisplayName}");
        else
            Console.WriteLine($"{Path.GetFileName(f),-28} (unreadable manifest)");
    }
    return 0;
}

static int RunVerify(ReadOnlySpan<string> rest)
{
    if (!TryDir(rest, "verify", out var dir)) return 2;
    var files = Directory.EnumerateFiles(dir, "*.idea").OrderBy(f => f, StringComparer.Ordinal).ToList();
    if (files.Count == 0) { Console.WriteLine("(no .idea packages found)"); return 0; }

    // Build the set of citizens this folder provides: (kind,key) -> versions present.
    var available = new Dictionary<(string Kind, string Key), SortedSet<int>>();
    var manifests = new List<IdeaManifest>();
    foreach (var f in files)
    {
        using var r = IdeaArchiveReader.Open(f);
        if (!r.TryReadManifest(out var m, out var err) || m is null)
        { Console.Error.WriteLine($"  unreadable: {Path.GetFileName(f)} ({err})"); continue; }
        manifests.Add(m);
        var id = (m.Category.ToLowerInvariant(), m.Key.ToLowerInvariant());
        if (!available.TryGetValue(id, out var set)) available[id] = set = new();
        set.Add(m.Version);
    }

    Console.WriteLine($"verify: {manifests.Count} package(s) in {dir}");
    var unresolved = 0;
    foreach (var m in manifests.OrderBy(x => x.Category).ThenBy(x => x.Key))
    {
        if (m.Uses.Count == 0) continue;
        Console.WriteLine($"  {m.Category}.{m.Key}@{m.Version}");
        foreach (var use in m.Uses)
        {
            // "<Kind>.<key>[@<version>]"  e.g. Plugin.tooltip  |  Component.tabs@1
            var spec = use.Trim();
            int? wantVersion = null;
            var at = spec.IndexOf('@');
            if (at >= 0) { if (int.TryParse(spec[(at + 1)..], out var v)) wantVersion = v; spec = spec[..at]; }
            var dot = spec.IndexOf('.');
            if (dot <= 0 || dot >= spec.Length - 1) { Console.WriteLine($"      ?? {use}  (unparseable)"); unresolved++; continue; }
            var id = (spec[..dot].ToLowerInvariant(), spec[(dot + 1)..].ToLowerInvariant());

            bool ok = available.TryGetValue(id, out var have)
                      && (wantVersion is null ? have!.Count > 0 : have!.Contains(wantVersion.Value));
            if (ok)
            {
                var resolved = wantVersion ?? available[id].Max;
                Console.WriteLine($"      ok {use}  -> {spec}.V{resolved}");
            }
            else
            {
                Console.WriteLine($"      MISSING {use}  (no {spec}" + (wantVersion is null ? "" : $"@{wantVersion}") + " in this folder)");
                unresolved++;
            }
        }
    }

    Console.WriteLine();
    if (unresolved == 0)
    {
        Console.WriteLine("verify: OK — every declared dependency resolves; the packages compose cleanly.");
        return 0;
    }
    Console.Error.WriteLine($"verify: {unresolved} unresolved dependency(ies). Add the missing .idea(s) to this folder.");
    return 1;
}

static int RunInstall(ReadOnlySpan<string> rest)
{
    var (file, allowOverride) = ParseFileAndFlags(rest, "install");
    if (file is null) return 2;
    using var reader = IdeaArchiveReader.Open(file);
    if (!reader.TryReadManifest(out var m, out var err)) { Console.Error.WriteLine($"ma-idea install: {err}"); return 1; }

    var result = ManifestValidator.Validate(m!, reader.BinEntries());
    foreach (var e in result.Errors)
        Console.Error.WriteLine($"  {(e.IsWarning ? "warning" : "error")} {e.Code}: {e.Message}");
    if (!result.IsValid) { Console.Error.WriteLine("ma-idea install: package is invalid (validate-only; nothing was installed)."); return 1; }

    Console.WriteLine($"ma-idea install: '{m!.Key}' v{m.Version} ({m.Kind}) is valid; would install"
        + (allowOverride ? " (override requested)." : "."));
    Console.WriteLine("(offline validate only — applying the install is a host operation: PackageInstallService.)");
    return 0;
}

static int RunUpgrade(ReadOnlySpan<string> rest)
{
    var (file, _) = ParseFileAndFlags(rest, "upgrade");
    if (file is null) return 2;
    using var reader = IdeaArchiveReader.Open(file);
    if (!reader.TryReadManifest(out var m, out var err)) { Console.Error.WriteLine($"ma-idea upgrade: {err}"); return 1; }
    var result = ManifestValidator.Validate(m!, reader.BinEntries());
    if (!result.IsValid) { Console.Error.WriteLine($"ma-idea upgrade: {result.Summary}"); return 1; }

    // Plan against the other .idea files in the same directory (offline preview of the host's decision).
    var dir = Path.GetDirectoryName(Path.GetFullPath(file))!;
    var installed = new List<InstalledRef>();
    foreach (var other in Directory.EnumerateFiles(dir, "*.idea"))
    {
        if (string.Equals(Path.GetFullPath(other), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)) continue;
        using var r = IdeaArchiveReader.Open(other);
        if (r.TryReadManifest(out var om, out _) && om is not null)
        {
            var otherSha = Sha256Hasher.OfBytes(PackageSigner.CanonicalManifest(r));
            installed.Add(new InstalledRef(om.Category, om.Key, om.Version, Enabled: true, IsActiveVersion: true, otherSha));
        }
    }

    var candidateSha = Sha256Hasher.OfBytes(PackageSigner.CanonicalManifest(reader));
    var plan = PackageVersionResolver.Plan(m!, candidateSha, installed, compiledKeyExists: false, allowOverride: false);
    Console.WriteLine($"ma-idea upgrade: {m!.Key} v{m.Version} -> {plan.Action}{(plan.Reason is null ? "" : " (" + plan.Reason + ")")}");
    return plan.Action is InstallAction.RejectDowngrade or InstallAction.Blocked or InstallAction.HashConflict ? 1 : 0;
}

static int RunDisable()
{
    Console.Error.WriteLine(
        "ma-idea disable: disabling an installed package is a host database operation (soft-disable, " +
        "Enabled=false; bytes are retained). Use the admin UI / PackageInstallService.DisableAsync — " +
        "the offline CLI cannot reach the site database.");
    return 2;
}

static int RunSign(ReadOnlySpan<string> rest)
{
    if (!TryFile(rest, "sign", out var file)) return 2;
    var opts = ArgParser.Parse(rest);
    var pfxPath = opts.GetValueOrDefault("pfx");
    var password = opts.GetValueOrDefault("password");

    if (string.IsNullOrWhiteSpace(pfxPath))
    { Console.Error.WriteLine("ma-idea sign: --pfx <cert.pfx> is required."); return 2; }
    if (!File.Exists(pfxPath))
    { Console.Error.WriteLine($"ma-idea sign: pfx not found: {pfxPath}"); return 2; }

    using var cert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, password);
    PackageSigner.SignFile(file, cert);
    Console.WriteLine($"ma-idea sign: signed {Path.GetFileName(file)} (signer thumbprint {cert.Thumbprint}).");
    return 0;
}

static int RunNupkg(ReadOnlySpan<string> rest)
{
    var opts = ArgParser.Parse(rest);
    var ideaPath = opts.GetValueOrDefault("idea");
    var outDir = opts.GetValueOrDefault("out");
    if (string.IsNullOrWhiteSpace(ideaPath) || string.IsNullOrWhiteSpace(outDir))
    { Console.Error.WriteLine("ma-idea nupkg: --idea <file.idea> and --out <dir> are required."); return 2; }
    if (!File.Exists(ideaPath))
    { Console.Error.WriteLine($"ma-idea nupkg: file not found: {ideaPath}"); return 2; }

    using var reader = IdeaArchiveReader.Open(ideaPath);
    if (!reader.TryReadManifest(out var m, out var err) || m is null)
    { Console.Error.WriteLine($"ma-idea nupkg: {err}"); return 1; }

    // The one mapping every install path relies on: Kind.key@version <-> IdeaCms.{Category}.{Key} @ {n}.0.0.
    var packageId = $"IdeaCms.{m.Category}.{m.Key}";
    var nugetVersion = new NuGetVersion(m.Version, 0, 0);

    var builder = new PackageBuilder
    {
        Id = packageId,
        Version = nugetVersion,
        Description = string.IsNullOrWhiteSpace(m.Description) ? m.DisplayName : m.Description,
    };
    builder.Authors.Add("MindAttic");
    builder.Files.Add(new PhysicalPackageFile
    {
        SourcePath = Path.GetFullPath(ideaPath),
        TargetPath = $"content/{m.Key}.idea",
    });

    var outDirFull = Path.GetFullPath(outDir);
    Directory.CreateDirectory(outDirFull);
    var outPath = Path.Combine(outDirFull, $"{packageId}.{nugetVersion}.nupkg");
    using (var fs = File.Create(outPath))
        builder.Save(fs);

    Console.WriteLine($"ma-idea nupkg: wrote {outPath}");
    return 0;
}

// ---- small CLI helpers ----

static bool TryFile(ReadOnlySpan<string> rest, string verb, out string file)
{
    file = "";
    if (rest.Length == 0 || rest[0].StartsWith("--", StringComparison.Ordinal))
    { Console.Error.WriteLine($"ma-idea {verb}: a <file.idea> path is required."); return false; }
    file = rest[0];
    if (!File.Exists(file)) { Console.Error.WriteLine($"ma-idea {verb}: file not found: {file}"); return false; }
    return true;
}

static bool TryDir(ReadOnlySpan<string> rest, string verb, out string dir)
{
    dir = rest.Length > 0 && !rest[0].StartsWith("--", StringComparison.Ordinal) ? rest[0] : ".";
    if (!Directory.Exists(dir)) { Console.Error.WriteLine($"ma-idea {verb}: directory not found: {dir}"); return false; }
    return true;
}

static (string? File, bool AllowOverride) ParseFileAndFlags(ReadOnlySpan<string> rest, string verb)
{
    if (!TryFile(rest, verb, out var file)) return (null, false);
    var allowOverride = false;
    foreach (var a in rest) if (string.Equals(a, "--allow-override", StringComparison.OrdinalIgnoreCase)) allowOverride = true;
    return (file, allowOverride);
}

static void PrintHelp() => Console.WriteLine("""
    ma-idea — IdeaCms .idea CLI

    Usage:
      ma-idea pack    --assembly <path> --out <dir> [--wwwroot <dir>] [--data <dir>] [--icon <file>] [--version <n>] [--refs <a;b>]
      ma-idea inspect <file.idea>
      ma-idea list    [dir]
      ma-idea verify  [dir]
      ma-idea install <file.idea> [--allow-override]
      ma-idea upgrade <file.idea>
      ma-idea sign    <file.idea> --pfx <cert.pfx> [--password <pw>]
      ma-idea nupkg   --idea <file.idea> --out <dir>

    pack    Pack a built Page/Theme/Plugin/Component RCL into a .idea (reflection-only).
    inspect Print a package's manifest + bin/ + wwwroot/ counts.
    list    List the .idea packages in a directory (key, version, category, kind).
    verify  Check every package's uses[] resolves against the .idea files in a directory (compose-graph check).
    install Validate a package offline (does NOT install — that is a host operation).
    upgrade Validate + preview the install action against the .idea files beside it.
    sign    Content-sign a .idea in place with a pfx (independent of NuGet's own signing feature).
    nupkg   Wrap an already-signed .idea into a IdeaCms.{Category}.{Key} .nupkg for publishing.
    """);
