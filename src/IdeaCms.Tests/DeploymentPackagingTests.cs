using System.Text.RegularExpressions;

namespace IdeaCms.Tests;

/// <summary>
/// M.IDEA-§4.14. A GitHub runner has no <c>C:\LocalNuGet</c> and no <c>../local-feed</c>, and NuGet
/// tolerates a missing local source <i>silently</i> — so a MindAttic package that was bumped in a
/// csproj but never vendored into <c>lib/local-packages/</c> does not fail here, it fails in CI with
/// a confusing NU1101 about a package that plainly exists on the dev box. This fixture closes that
/// gap: it is the only thing standing between a one-line version bump and a red deploy.
/// </summary>
[TestFixture]
public class DeploymentPackagingTests
{
    private static readonly Regex MindAtticPackageRef = new(
        @"<PackageReference\s+Include=""(?<id>MindAttic\.[^""]+)""\s+Version=""(?<version>[^""]+)""",
        RegexOptions.Compiled);

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IdeaCms.slnx")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "Could not locate the repo root (IdeaCms.slnx).");
        return dir!;
    }

    private static IEnumerable<(string Project, string Id, string Version)> ReferencedMindAtticPackages()
    {
        var srcDir = Path.Combine(RepoRoot().FullName, "src");
        foreach (var csproj in Directory.EnumerateFiles(srcDir, "*.csproj", SearchOption.AllDirectories))
        {
            foreach (Match m in MindAtticPackageRef.Matches(File.ReadAllText(csproj)))
            {
                yield return (Path.GetFileName(csproj), m.Groups["id"].Value, m.Groups["version"].Value);
            }
        }
    }

    [Test]
    public void EveryReferencedMindAtticPackageIsVendoredForCi()
    {
        var vendorDir = Path.Combine(RepoRoot().FullName, "lib", "local-packages");
        Assert.That(Directory.Exists(vendorDir), Is.True, $"Missing vendored feed at {vendorDir}.");

        var referenced = ReferencedMindAtticPackages().ToList();
        Assert.That(referenced, Is.Not.Empty, "Expected at least one MindAttic PackageReference.");

        var missing = referenced
            .Where(r => !File.Exists(Path.Combine(vendorDir, $"{r.Id}.{r.Version}.nupkg")))
            .Select(r => $"{r.Id} {r.Version} (referenced by {r.Project})")
            .Distinct()
            .ToList();

        Assert.That(missing, Is.Empty,
            "These packages are referenced but not vendored, so a CI restore will fail. Copy the "
            + $".nupkg into lib/local-packages/:{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", missing));
    }

    [Test]
    public void VendoredPackagesMatchTheLocalFeedsSameVersion()
    {
        // M.IDEA-T34: MindAttic packages are repacked at the same version while a major is rolling out. A vendored copy
        // left from an earlier pack restores instead of the current one (local-packages is listed first), so the
        // deployed Release build silently runs stale library code. Only checkable where the dev feed exists.
        const string feed = @"C:\LocalNuGet";
        if (!Directory.Exists(feed)) Assert.Ignore("No local feed on this machine (CI).");
        var vendorDir = Path.Combine(RepoRoot().FullName, "lib", "local-packages");

        var stale = ReferencedMindAtticPackages()
            .Select(r => $"{r.Id}.{r.Version}.nupkg")
            .Distinct()
            .Where(f => File.Exists(Path.Combine(vendorDir, f)) && File.Exists(Path.Combine(feed, f)))
            .Where(f => !File.ReadAllBytes(Path.Combine(vendorDir, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(feed, f))))
            .ToList();

        Assert.That(stale, Is.Empty, $"Vendored packages differ from {feed}; copy the current .nupkg into lib/local-packages/: "
            + string.Join(", ", stale));
    }

    [Test]
    public void NugetConfigListsTheVendoredFeed()
    {
        var config = File.ReadAllText(Path.Combine(RepoRoot().FullName, "nuget.config"));

        Assert.That(config, Does.Contain("./lib/local-packages"),
            "nuget.config must list the vendored feed, or CI has no source for the MindAttic packages.");
    }

    [Test]
    public void VendoredPackagesAreTrackedRatherThanGitIgnored()
    {
        var gitignore = File.ReadAllText(Path.Combine(RepoRoot().FullName, ".gitignore"));

        Assert.That(gitignore, Does.Contain("!lib/local-packages/*.nupkg"),
            ".gitignore excludes *.nupkg globally; the vendored feed must be re-included or it never "
            + "reaches the runner.");
    }

    [Test]
    public void DeployWorkflowPointsAtProjectsThatExist()
    {
        var root = RepoRoot().FullName;
        var workflowPath = Path.Combine(root, ".github", "workflows", "azure-deploy.yml");
        Assert.That(File.Exists(workflowPath), Is.True, "Missing .github/workflows/azure-deploy.yml.");

        var workflow = File.ReadAllText(workflowPath);

        // Paths the workflow hands to dotnet. A rename that misses the workflow is a red deploy.
        string[] mustExist =
        [
            "src/IdeaCms.Blazor/IdeaCms.Blazor.csproj",
            "src/IdeaCms.Tests/IdeaCms.Tests.csproj",
            "src/IdeaCms.Core",
            "IdeaCms.slnx",
        ];

        Assert.Multiple(() =>
        {
            foreach (var relative in mustExist)
            {
                Assert.That(workflow, Does.Contain(relative), $"Workflow no longer references {relative}.");
                var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Assert.That(File.Exists(full) || Directory.Exists(full), Is.True,
                    $"Workflow references {relative}, which does not exist.");
            }
        });
    }

    /// <summary>
    /// Floors for the three packages bumped to clear security advisories (M.IDEA-§4.14). Pinned versions
    /// are easy to revert by accident during a merge, and a downgrade silently reintroduces the
    /// advisory — nothing else in the build would notice.
    /// </summary>
    [TestCase("AngleSharp", "1.7.2", "GHSA-pgww-w46g-26qg")]
    [TestCase("HtmlSanitizer", "9.2.1039", "requires the patched AngleSharp line")]
    [TestCase("System.Security.Cryptography.Xml", "10.0.11", "five HIGH advisories against 10.0.8")]
    public void SecurityPinnedPackagesAreNotDowngraded(string id, string minimum, string why)
    {
        var pattern = new Regex(
            $@"<PackageReference\s+Include=""{Regex.Escape(id)}""\s+Version=""(?<version>[^""]+)""",
            RegexOptions.Compiled);

        var srcDir = Path.Combine(RepoRoot().FullName, "src");
        var found = new List<(string Project, Version Version)>();

        foreach (var csproj in Directory.EnumerateFiles(srcDir, "*.csproj", SearchOption.AllDirectories))
        {
            foreach (Match m in pattern.Matches(File.ReadAllText(csproj)))
            {
                if (Version.TryParse(m.Groups["version"].Value, out var parsed))
                    found.Add((Path.GetFileName(csproj), parsed));
            }
        }

        Assert.That(found, Is.Not.Empty, $"Expected a pinned PackageReference for {id}.");

        var floor = Version.Parse(minimum);
        foreach (var (project, version) in found)
        {
            Assert.That(version, Is.GreaterThanOrEqualTo(floor),
                $"{project} pins {id} {version}, below the {minimum} floor ({why}).");
        }
    }

    [Test]
    public void ProductionRequiresItsDataProtectionSettingsByName()
    {
        var program = File.ReadAllText(Path.Combine(
            RepoRoot().FullName, "src", "IdeaCms.Blazor", "Program.cs"));

        Assert.Multiple(() =>
        {
            // The app fail-closes in production without these. docs/DEPLOYMENT.md documents both by
            // name; if a rename lands here the runbook silently becomes wrong.
            Assert.That(program, Does.Contain("DataProtection:BlobUri"));
            Assert.That(program, Does.Contain("DataProtection:KeyVaultKeyId"));
            // App Service health-checks this path (infra/main.bicep healthCheckPath).
            Assert.That(program, Does.Contain("/_health"));
        });
    }

    /// <summary>
    /// M.IDEA-§4.14 startup. The server must be listening before the boot sequence runs, and the boot sequence
    /// must go through <c>StartupGate</c> (retry transient, exit 1 on permanent) rather than run bare in
    /// top-level statements, where an unhandled SQL login failure aborted the runtime with exit 134.
    /// </summary>
    [Test]
    public void TheServerListensBeforeTheBootSequenceAndTheBootSequenceIsRetried()
    {
        var program = File.ReadAllText(Path.Combine(
            RepoRoot().FullName, "src", "IdeaCms.Blazor", "Program.cs"));

        var start = program.IndexOf("await app.StartAsync();", StringComparison.Ordinal);
        var init = program.IndexOf("StartupGate.RunWithRetryAsync(InitializeAsync", start < 0 ? 0 : start, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "Program.cs must start the server explicitly.");
            Assert.That(init, Is.GreaterThan(start), "initialisation must run after the server is listening");
            Assert.That(program, Does.Not.Contain("app.Run();"), "app.Run() would block before or skip the gated boot");
            Assert.That(program, Does.Contain("StartupGate.Gate(readiness"), "requests must be gated until ready");
            Assert.That(program, Does.Contain("Environment.ExitCode = 1"), "a failed boot exits 1, not SIGABRT");
        });
    }

    /// <summary>
    /// M.IDEA-§4.14 deploy. The company site is deployed, restarted and proven ready on THIS commit before the
    /// demo is touched, so the two never cold-start together on the single B1 core; and a bare 200 from the
    /// container being replaced cannot pass the smoke test.
    /// </summary>
    [Test]
    public void DeployProvesTheCompanySiteReadyOnThisCommitBeforeTouchingTheDemo()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot().FullName, ".github", "workflows", "azure-deploy.yml"));

        int At(string marker)
        {
            var i = workflow.IndexOf(marker, StringComparison.Ordinal);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), $"azure-deploy.yml no longer contains: {marker}");
            return i;
        }

        var company = At("  deploy-company:");
        var smoke = At("X-IdeaCms-Ready");
        var demo = At("  deploy-demo:");

        Assert.Multiple(() =>
        {
            Assert.That(workflow, Does.Contain("-p:SourceRevisionId=${{ github.sha }}"), "the build must carry the commit /_health reports");
            Assert.That(workflow, Does.Contain("needs: [build, migrate, deploy-company]"), "the demo deploys after the company site");
            Assert.That(workflow, Does.Not.Contain("matrix:"), "a matrix restarts both sites at once");
            Assert.That(company, Is.LessThan(smoke));
            Assert.That(smoke, Is.LessThan(demo), "the company smoke test belongs to the company job");
            Assert.That(workflow, Does.Contain("$version -like \"*$sha*\""), "wait for this commit, not any 200");
        });
    }

    /// <summary>
    /// M.IDEA-§4.14 demo reset. The app seeds <c>admin</c> once, on the first boot that finds no users, from
    /// the bootstrap token it started with. So the demo must be STOPPED before its database is replaced,
    /// the new token pinned before that database exists, and the demo started only afterwards: otherwise a
    /// boot in between seeds the previous hour's password (and the old container answers the sign-in
    /// check), which is exactly how every hourly run failed with a 302.
    /// </summary>
    [Test]
    public void DemoResetStopsTheDemoAndPinsTheNewPasswordBeforeReplacingItsDatabase()
    {
        var path = Path.Combine(RepoRoot().FullName, ".github", "workflows", "demo-reset.yml");
        var workflow = File.ReadAllText(path);

        int At(string marker)
        {
            var i = workflow.IndexOf(marker, StringComparison.Ordinal);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), $"demo-reset.yml no longer contains: {marker}");
            return i;
        }

        var newPassword = At("--name admin-password");
        var stop = At("az webapp stop");
        var pin = At("MindAttic__Vault__Security__bootstraptoken=@Microsoft.KeyVault(SecretUri=$env:ADMIN_PASSWORD_URI)");
        var dropDb = At("az sql db delete");
        var copyDb = At("az sql db copy");
        var start = At("az webapp start");
        var signIn = At("/_ma-auth/login");
        var publish = At("status = 'ready'");

        Assert.Multiple(() =>
        {
            Assert.That(newPassword, Is.LessThan(pin), "the password must exist before it is pinned");
            Assert.That(stop, Is.LessThan(pin), "pin the token on a stopped site (a settings change restarts a running one)");
            Assert.That(pin, Is.LessThan(dropDb), "the new token must be in place before the empty database exists");
            Assert.That(dropDb, Is.LessThan(copyDb));
            Assert.That(copyDb, Is.LessThan(start), "nothing may boot against the fresh database before the start");
            Assert.That(start, Is.LessThan(signIn));
            Assert.That(signIn, Is.LessThan(publish), "publish the login only after signing in with it");
            Assert.That(workflow.Split("az webapp start").Length - 1, Is.EqualTo(1), "exactly one start, after the database swap");
            Assert.That(workflow.Split("bootstraptoken=").Length - 1, Is.EqualTo(1), "the token is pinned once, before the swap");
        });
    }
}
