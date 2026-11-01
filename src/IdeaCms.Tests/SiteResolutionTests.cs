using Microsoft.EntityFrameworkCore;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using IdeaCms.Core.Services;
using IdeaCms.Core.Sites;

namespace IdeaCms.Tests;

/// <summary>
/// One deployment, many domains. <c>Site.HostBindings</c> has been in the schema since migration #1
/// and drives site resolution (M.IDEA-§4.10), so these tests pin the two things that decide whether turning
/// it on is safe: an unbound host still lands on the default site (every existing single-site install
/// must behave exactly as before), and a bound host lands on its own site and nowhere else.
/// </summary>
[TestFixture]
public class SiteResolutionTests
{
    private static Site S(int id, string key, string bindings, bool isDefault = false) =>
        new() { Id = id, Key = key, Name = key, HostBindings = bindings, IsDefault = isDefault };

    // ---- the matching rule -----------------------------------------------------------------

    [TestCase("mindattic.com", "mindattic.com", HostBinding.MatchQuality.Host)]
    [TestCase("MindAttic.COM", "mindattic.com", HostBinding.MatchQuality.Host)]
    [TestCase("mindattic.com.", "mindattic.com", HostBinding.MatchQuality.Host)]
    [TestCase("mindattic.com", "  mindattic.com  ", HostBinding.MatchQuality.Host)]
    [TestCase("mindattic.com", "https://mindattic.com/", HostBinding.MatchQuality.Host)]
    [TestCase("other.com", "mindattic.com", HostBinding.MatchQuality.None)]
    [TestCase("mindattic.com", "", HostBinding.MatchQuality.None)]
    [TestCase("mindattic.com", "a.com, mindattic.com ; b.com", HostBinding.MatchQuality.Host)]
    public void HostnameMatching(string requestHost, string bindings, HostBinding.MatchQuality expected) =>
        Assert.That(HostBinding.Match(bindings, requestHost), Is.EqualTo(expected));

    [Test]
    public void APortlessBindingIgnoresThePort_SoAProductionBindingStillMatchesOnLocalhost()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HostBinding.Match("mindattic.com", "mindattic.com:5199"),
                Is.EqualTo(HostBinding.MatchQuality.Host));
            Assert.That(HostBinding.Match("mindattic.com", "mindattic.com:443"),
                Is.EqualTo(HostBinding.MatchQuality.Host));
        });
    }

    [Test]
    public void ABindingThatNamesAPortMustMatchIt_AndOutranksThePortlessOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HostBinding.Match("localhost:5199", "localhost:5199"),
                Is.EqualTo(HostBinding.MatchQuality.HostAndPort));
            Assert.That(HostBinding.Match("localhost:5199", "localhost:5200"),
                Is.EqualTo(HostBinding.MatchQuality.None),
                "an explicit port is a constraint, not a hint");
            Assert.That(HostBinding.MatchQuality.HostAndPort, Is.GreaterThan(HostBinding.MatchQuality.Host));
        });
    }

    [Test]
    public void WildcardCoversSubdomainsButNeverTheApex()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HostBinding.Match("*.mindattic.com", "www.mindattic.com"),
                Is.EqualTo(HostBinding.MatchQuality.Wildcard));
            Assert.That(HostBinding.Match("*.mindattic.com", "deep.nested.mindattic.com"),
                Is.EqualTo(HostBinding.MatchQuality.Wildcard));
            Assert.That(HostBinding.Match("*.mindattic.com", "mindattic.com"),
                Is.EqualTo(HostBinding.MatchQuality.None),
                "a wildcard must never silently claim the apex another site may own");
            Assert.That(HostBinding.Match("*.mindattic.com", "notmindattic.com"),
                Is.EqualTo(HostBinding.MatchQuality.None),
                "suffix matching must respect the dot boundary");
            Assert.That(HostBinding.Match("*", "anything.at.all"),
                Is.EqualTo(HostBinding.MatchQuality.CatchAll));
        });
    }

    [Test]
    public void AnIpv6LiteralIsNotMistakenForAHostAndPort()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HostBinding.SplitHostPort("[::1]:5199"), Is.EqualTo(("[::1]", "5199")));
            Assert.That(HostBinding.SplitHostPort("[::1]"), Is.EqualTo(("[::1]", "")));
            Assert.That(HostBinding.Match("[::1]", "[::1]:5199"), Is.EqualTo(HostBinding.MatchQuality.Host));
        });
    }

    // ---- choosing a site -------------------------------------------------------------------

    [Test]
    public void AnUnboundHostFallsBackToTheDefaultSite()
    {
        var sites = new[] { S(1, "default", "", isDefault: true), S(2, "other", "other.com") };
        Assert.That(SiteResolver.Resolve("unknown.example", sites)?.Key, Is.EqualTo("default"));
    }

    [Test]
    public void TheExistingSingleSiteInstallIsUnaffected()
    {
        // Exactly the seeded shape: one site, no bindings. It must answer on every hostname, or
        // multi-domain resolution would silently 404 every single-site deployment.
        var sites = new[] { S(1, "default", "", isDefault: true) };
        Assert.Multiple(() =>
        {
            Assert.That(SiteResolver.Resolve("localhost:5199", sites)?.Key, Is.EqualTo("default"));
            Assert.That(SiteResolver.Resolve("mindattic-ideas.azurewebsites.net", sites)?.Key, Is.EqualTo("default"));
            Assert.That(SiteResolver.Resolve(null, sites)?.Key, Is.EqualTo("default"));
            Assert.That(SiteResolver.Resolve("", sites)?.Key, Is.EqualTo("default"));
        });
    }

    [Test]
    public void ABoundHostWinsOverTheDefaultSite()
    {
        var sites = new[] { S(1, "default", "", isDefault: true), S(2, "rdb", "ryandebraal.com, www.ryandebraal.com") };
        Assert.Multiple(() =>
        {
            Assert.That(SiteResolver.Resolve("ryandebraal.com", sites)?.Key, Is.EqualTo("rdb"));
            Assert.That(SiteResolver.Resolve("www.ryandebraal.com:5199", sites)?.Key, Is.EqualTo("rdb"));
            Assert.That(SiteResolver.Resolve("mindattic.com", sites)?.Key, Is.EqualTo("default"));
        });
    }

    [Test]
    public void AnExactBindingBeatsAWildcardOnAnotherSite()
    {
        var sites = new[]
        {
            S(1, "default", "", isDefault: true),
            S(2, "wild", "*.mindattic.com"),
            S(3, "blog", "blog.mindattic.com"),
        };
        Assert.Multiple(() =>
        {
            Assert.That(SiteResolver.Resolve("blog.mindattic.com", sites)?.Key, Is.EqualTo("blog"),
                "the specific binding must win, or a wildcard site would swallow its siblings");
            Assert.That(SiteResolver.Resolve("shop.mindattic.com", sites)?.Key, Is.EqualTo("wild"));
        });
    }

    [Test]
    public void ACatchAllSiteLosesToEveryRealBinding()
    {
        var sites = new[] { S(1, "catch", "*"), S(2, "real", "mindattic.com"), S(3, "sub", "*.mindattic.com") };
        Assert.Multiple(() =>
        {
            Assert.That(SiteResolver.Resolve("mindattic.com", sites)?.Key, Is.EqualTo("real"));
            Assert.That(SiteResolver.Resolve("www.mindattic.com", sites)?.Key, Is.EqualTo("sub"));
            Assert.That(SiteResolver.Resolve("elsewhere.net", sites)?.Key, Is.EqualTo("catch"));
        });
    }

    [Test]
    public void ResolutionIsStableWhenTwoSitesTie()
    {
        // Same quality on both: the answer must not depend on row order.
        var a = new[] { S(1, "one", "*"), S(2, "two", "*", isDefault: true) };
        var b = new[] { S(2, "two", "*", isDefault: true), S(1, "one", "*") };
        Assert.Multiple(() =>
        {
            Assert.That(SiteResolver.Resolve("x.com", a)?.Key, Is.EqualTo("two"), "the default site breaks the tie");
            Assert.That(SiteResolver.Resolve("x.com", b)?.Key, Is.EqualTo("two"));
        });
    }

    [Test]
    public void NoSitesResolvesToNullRatherThanThrowing() =>
        Assert.That(SiteResolver.Resolve("anything", []), Is.Null);

    // ---- the admin service ------------------------------------------------------------------

    private static CmsDbContext NewDb() =>
        new(new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase("sites_" + Guid.NewGuid().ToString("N")).Options);

    [Test]
    public async Task CreatingASite_NormalizesItsBindings_AndDoesNotStealDefault()
    {
        await using var db = NewDb();
        db.Sites.Add(new Site { Key = "default", Name = "MindAttic", IsDefault = true });
        await db.SaveChangesAsync();

        var svc = new SiteAdminService(db);
        var (ok, error, id) = await svc.CreateAsync("rdb", "Ryan DeBraal",
            " RyanDeBraal.com ,, https://www.ryandebraal.com/ ", "dark", 1, "dark");

        var created = await db.Sites.SingleAsync(s => s.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True, error);
            Assert.That(created.HostBindings, Is.EqualTo("ryandebraal.com, www.ryandebraal.com"),
                "bindings are stored normalized, so matching never has to re-clean them");
            Assert.That(created.IsDefault, Is.False, "creating a site must not move the default");
        });
    }

    [Test]
    public async Task TwoSitesCannotClaimTheSameHostname()
    {
        await using var db = NewDb();
        db.Sites.Add(new Site { Key = "default", Name = "d", IsDefault = true, HostBindings = "mindattic.com" });
        await db.SaveChangesAsync();

        var svc = new SiteAdminService(db);
        var (ok, error, _) = await svc.CreateAsync("dupe", "Dupe", "MINDATTIC.com", "", 1, "light");

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False, "the loser of a duplicate binding would be invisible with no error anywhere");
            Assert.That(error, Does.Contain("already bound"));
        });
    }

    [Test]
    public async Task TheDefaultSiteCannotBeDeleted_AndNeitherCanOneThatStillHasPages()
    {
        await using var db = NewDb();
        var def = new Site { Key = "default", Name = "d", IsDefault = true };
        var other = new Site { Key = "other", Name = "o" };
        db.Sites.AddRange(def, other);
        await db.SaveChangesAsync();
        db.Pages.Add(new Core.Entities.Page { SiteId = other.Id, Slug = "x", Title = "x" });
        await db.SaveChangesAsync();

        var svc = new SiteAdminService(db);
        var deleteDefault = await svc.DeleteAsync(def.Id);
        var deleteOccupied = await svc.DeleteAsync(other.Id);

        Assert.Multiple(() =>
        {
            Assert.That(deleteDefault.Ok, Is.False);
            Assert.That(deleteDefault.Error, Does.Contain("default site"));
            Assert.That(deleteOccupied.Ok, Is.False, "deleting a site would orphan its pages onto another domain");
            Assert.That(deleteOccupied.Error, Does.Contain("1 page"));
        });
    }

    [Test]
    public async Task MakeDefault_LeavesExactlyOneDefault()
    {
        await using var db = NewDb();
        var a = new Site { Key = "a", Name = "a", IsDefault = true };
        var b = new Site { Key = "b", Name = "b" };
        db.Sites.AddRange(a, b);
        await db.SaveChangesAsync();

        var svc = new SiteAdminService(db);
        Assert.That((await svc.MakeDefaultAsync(b.Id)).Ok, Is.True);

        var defaults = await db.Sites.Where(s => s.IsDefault).Select(s => s.Key).ToListAsync();
        Assert.That(defaults, Is.EqualTo(new[] { "b" }),
            "two defaults would make every unbound host depend on row order");
    }

    [Test]
    public async Task TheResolverAndTheAdminProbeAgree()
    {
        await using var db = NewDb();
        db.Sites.AddRange(
            new Site { Key = "default", Name = "d", IsDefault = true },
            new Site { Key = "rdb", Name = "Ryan", HostBindings = "ryandebraal.com" });
        await db.SaveChangesAsync();

        var resolved = await new SiteResolver().ResolveAsync("ryandebraal.com", db);
        var probed = await new SiteAdminService(db).WhichSiteAsync("ryandebraal.com");

        Assert.Multiple(() =>
        {
            Assert.That(resolved?.Key, Is.EqualTo("rdb"));
            Assert.That(probed?.Key, Is.EqualTo("rdb"),
                "the panel's probe must answer with the same rule the render path uses");
        });
    }

    // ---- the interactive-circuit trap ------------------------------------------------------

    /// <summary>
    /// PageHost is <c>@rendermode InteractiveServer</c>, so <c>IHttpContextAccessor.HttpContext</c> is
    /// non-null during prerender and NULL for every render after the circuit connects. Resolving the
    /// host from it therefore works on first paint and silently falls back to the DEFAULT site on
    /// every client-side navigation afterwards — a bug that presents as "the second domain works
    /// until you click a link", which no unit test of the resolver would ever catch.
    /// This pins the source so the reading cannot be "simplified" back.
    /// </summary>
    [Test]
    public void PageHostReadsTheRequestHostFromNavigationManager_NotHttpContext()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IdeaCms.slnx")))
            dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "Could not locate the repo root (IdeaCms.slnx).");

        var pageHost = Path.Combine(dir!.FullName, "src", "IdeaCms.Rendering", "PageHost.razor");
        Assert.That(File.Exists(pageHost), Is.True, pageHost);
        var source = File.ReadAllText(pageHost);

        var assignment = File.ReadAllLines(pageHost)
            .FirstOrDefault(l => l.Contains("var requestHost", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(assignment, Is.Not.Null, "PageHost must resolve a requestHost for ISiteResolver.");
            Assert.That(assignment, Does.Contain("Nav.BaseUri"),
                "the host must come from the circuit-safe NavigationManager");
            Assert.That(assignment!.IndexOf("Nav.BaseUri", StringComparison.Ordinal),
                Is.LessThan(assignment.IndexOf("Http.HttpContext", StringComparison.Ordinal) is var i && i < 0
                            ? int.MaxValue : i),
                "HttpContext may only ever be a FALLBACK behind NavigationManager, never the primary source");
            Assert.That(source, Does.Contain("SiteResolver.ResolveAsync"),
                "PageHost must resolve the site by host, not take the default site");
        });
    }
}
