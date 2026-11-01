using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MindAttic.Ideas.Abstractions;
using IdeaCms.Core.Data;
using IdeaCms.Core.Entities;
using CmsPage = IdeaCms.Core.Entities.Page;

namespace IdeaCms.Blazor.Cli;

/// <summary>
/// CLI mode: <c>--seed repos</c>. Gives every public, non-archived repo in a GitHub org its own CMS page
/// under <c>/projects</c>, driven by the GitHub API rather than a hand-maintained list — so the site tracks
/// the org instead of drifting from it.
/// <para>
/// Each page is a Data page whose body is <c>&lt;Component.frommd /&gt;</c>. The README markdown and the repo's
/// metadata (description, language, topics, stars, homepage) are snapshotted into <see cref="ComponentMetadata"/>,
/// so rendering never depends on a local clone, a network call, or the GitHub rate limit.
/// </para>
/// <para>
/// Re-runnable. Author edits are preserved: theme/plugin selections are only filled in when unset, and a repo
/// that disappears from the org soft-disables its page (HOUSE-LAW-2) instead of deleting content.
/// </para>
/// Usage: <c>dotnet run --project src/IdeaCms.Blazor -- --seed repos [--org mindattic]
/// [--local-root D:\Projects\MindAttic] [--dry-run]</c>
/// </summary>
public static class SeedReposCli
{
    private const string ProjectsSlug = "projects";
    private const string FromMdBody = "<Component.frommd />";

    /// <summary>The portfolio index: a heading plus the card grid over this page's children.</summary>
    private const string ProjectsIndexBody =
        """
        <h1>Projects</h1>
        <p class="ma-lede">Every public repository, with its README rendered as a page.</p>
        <Component.projectgrid />
        """;
    private const string MetaKeyReadme = "frommd";
    private const string MetaKeyRepo = "repo";

    /// <summary>Default chrome for a generated project page. Only applied when the page has none.</summary>
    private const string DefaultThemeKey = "cyberspace";
    private static readonly string[] DefaultPlugins =
        ["Plugin.navmenu", "Plugin.breadcrumbs", "Plugin.footer", "Plugin.backtotop", "Plugin.poweredby"];

    /// <summary>
    /// Plugin selections this seeder has shipped as its default. A page still carrying one verbatim has
    /// never been touched by an author, so it can be upgraded to the current default; anything else is a
    /// deliberate choice and is left exactly as it is.
    /// </summary>
    private static readonly string[][] StockPluginSets =
    [
        ["Plugin.navmenu", "Plugin.breadcrumbs", "Plugin.footer", "Plugin.backtotop"],
        DefaultPlugins,
    ];

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ---- GitHub API shapes (only the fields we consume) ----

    private sealed record GhRepo(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("homepage")] string? Homepage,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("stargazers_count")] int Stars,
        [property: JsonPropertyName("topics")] string[]? Topics,
        [property: JsonPropertyName("private")] bool Private,
        [property: JsonPropertyName("archived")] bool Archived,
        [property: JsonPropertyName("fork")] bool Fork,
        [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt);

    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        var dryRun = args.Contains("--dry-run");
        var org = ArgValue(args, "--org") ?? "mindattic";
        var localRoot = ArgValue(args, "--local-root");

        if (dryRun) Console.WriteLine("[seed-repos] DRY RUN — no DB writes.");
        Console.WriteLine($"[seed-repos] Org: {org}");

        using var http = CreateClient(services);

        List<GhRepo> repos;
        try
        {
            repos = await FetchReposAsync(http, org);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[seed-repos] Could not list repos for '{org}': {ex.Message}");
            return 1;
        }

        // Public, non-archived, non-fork — the portfolio surface. Forks are someone else's work.
        var live = repos
            .Where(r => !r.Private && !r.Archived && !r.Fork)
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Console.WriteLine($"[seed-repos] {live.Count} public, non-archived repos (of {repos.Count} total).");
        if (live.Count == 0)
        {
            Console.Error.WriteLine("[seed-repos] Nothing to seed — refusing to disable existing pages on an empty result.");
            return 1;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CmsDbContext>();

        var site = await db.Sites.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (site is null) { Console.Error.WriteLine("[seed-repos] No site found. Create a site first."); return 1; }
        Console.WriteLine($"[seed-repos] Site: {site.Key} (Id={site.Id})");

        var parent = await EnsureProjectsParentAsync(db, site.Id, dryRun);
        if (parent is null && !dryRun) return 1;

        // Existing project pages, indexed so a repo can be matched to the page it already owns even if the
        // slug convention changed since it was created.
        var existing = await db.Pages.IgnoreQueryFilters()
            .Where(p => p.SiteId == site.Id && p.Slug.StartsWith(ProjectsSlug + "/"))
            .ToListAsync();
        var repoNameByPageUid = await RepoNameByPageUidAsync(db);

        var seen = new HashSet<int>();
        int created = 0, updated = 0, stubbed = 0;

        foreach (var repo in live)
        {
            var slug = $"{ProjectsSlug}/{SlugFor(repo.Name)}";

            var page = FindExistingPage(existing, repoNameByPageUid, repo.Name, slug);
            var markdown = await ResolveReadmeAsync(http, org, repo.Name, localRoot);
            if (markdown is null)
            {
                // Every repo gets a page — a missing README is a thin page, not an absent one. The stub is
                // built from the GitHub description so the page still says what the project is.
                markdown = StubReadme(repo, org);
                Console.WriteLine($"[seed-repos]   ! {repo.Name}: no README — using a description stub.");
                stubbed++;
            }

            if (page is null)
            {
                if (dryRun) { Console.WriteLine($"[seed-repos] [DRY] create /{slug} ({repo.Name})"); created++; continue; }

                var now = DateTime.UtcNow;
                page = new CmsPage
                {
                    SiteId = site.Id,
                    ParentId = parent!.Id,
                    Slug = slug,
                    Title = TitleFor(repo.Name),
                    Kind = PageKind.Data,
                    BodyHtml = FromMdBody,
                    BodyTrust = ContentTrust.Author,
                    ThemeKey = DefaultThemeKey,
                    ActivePluginsJson = JsonSerializer.Serialize(DefaultPlugins),
                    SeoTitle = $"{TitleFor(repo.Name)} — MindAttic",
                    IsPublished = true,
                    Enabled = true,
                    CreatedUtc = now,
                    ModifiedUtc = now,
                };
                db.Pages.Add(page);
                await db.SaveChangesAsync();
                Console.WriteLine($"[seed-repos]   + /{slug} ({repo.Name})");
                created++;
            }
            else
            {
                if (dryRun) { Console.WriteLine($"[seed-repos] [DRY] update /{page.Slug} ({repo.Name})"); updated++; seen.Add(page.Id); continue; }

                // A repo rename moves the page and leaves the old slug redirecting, never 404ing.
                if (!string.Equals(page.Slug, slug, StringComparison.OrdinalIgnoreCase))
                {
                    await RecordSlugHistoryAsync(db, page.Id, page.Slug);
                    Console.WriteLine($"[seed-repos]   ~ /{page.Slug} -> /{slug} (redirect recorded)");
                    page.Slug = slug;

                    // The slug is derived from the repo name, so a slug change means the name changed —
                    // leaving the old title would keep showing e.g. "StreetSamurai" for a repo now called
                    // Prose. Titles are only refreshed here, so an author's rename is otherwise preserved.
                    page.Title = TitleFor(repo.Name);
                    page.SeoTitle = $"{page.Title} — MindAttic";
                }

                // Only fill in chrome the author hasn't chosen; never clobber an admin edit.
                page.ThemeKey ??= DefaultThemeKey;
                if (page.ActivePluginsJson is null || IsStockPluginSelection(page.ActivePluginsJson))
                    page.ActivePluginsJson = JsonSerializer.Serialize(DefaultPlugins);
                page.ParentId ??= parent!.Id;
                if (string.IsNullOrWhiteSpace(page.BodyHtml)) page.BodyHtml = FromMdBody;
                page.Enabled = true;
                page.ModifiedUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
                updated++;
            }

            seen.Add(page.Id);
            await UpsertMetadataAsync(db, page.Uid, MetaKeyReadme, JsonSerializer.Serialize(new
            {
                markdown,
                source = $"https://github.com/{org}/{repo.Name}",
                lastSynced = DateTime.UtcNow,
            }, JsonOpts));
            await UpsertMetadataAsync(db, page.Uid, MetaKeyRepo, JsonSerializer.Serialize(new
            {
                name = repo.Name,
                description = repo.Description ?? "",
                url = repo.HtmlUrl ?? $"https://github.com/{org}/{repo.Name}",
                homepage = repo.Homepage ?? "",
                language = repo.Language ?? "",
                stars = repo.Stars,
                topics = repo.Topics ?? [],
                pushedAt = repo.PushedAt,
                lastSynced = DateTime.UtcNow,
            }, JsonOpts));
        }

        // A repo that left the org soft-disables its page — reversible, and its history survives.
        var retired = 0;
        foreach (var page in existing.Where(p => !seen.Contains(p.Id) && p.Enabled))
        {
            if (dryRun) { Console.WriteLine($"[seed-repos] [DRY] disable /{page.Slug} (no matching repo)"); retired++; continue; }
            page.Enabled = false;
            page.ModifiedUtc = DateTime.UtcNow;
            Console.WriteLine($"[seed-repos]   - /{page.Slug} disabled (no matching repo)");
            retired++;
        }
        if (!dryRun && retired > 0) await db.SaveChangesAsync();

        Console.WriteLine($"[seed-repos] Done. created={created} updated={updated} disabled={retired} readme-stubs={stubbed}");
        return 0;
    }

    // ---- GitHub ----

    private static HttpClient CreateClient(IServiceProvider services)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IdeaCms", "1"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        // A token is optional: unauthenticated works but burns a 60/hour budget, which one full sync can exhaust.
        var config = services.GetService<IConfiguration>();
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                    ?? Environment.GetEnvironmentVariable("GH_TOKEN")
                    ?? config?["GitHub:Token"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Console.WriteLine("[seed-repos] Using an authenticated GitHub token.");
        }
        else
        {
            Console.WriteLine("[seed-repos] No GitHub token (set GITHUB_TOKEN) — using the 60/hour anonymous budget.");
        }
        return http;
    }

    /// <summary>
    /// All repos for an account. GitHub splits these across two endpoints and an account is one or the other,
    /// so try <c>/orgs/{name}</c> first and fall back to <c>/users/{name}</c> (mindattic is a user, not an org).
    /// </summary>
    private static async Task<List<GhRepo>> FetchReposAsync(HttpClient http, string account)
    {
        foreach (var owner in new[] { "orgs", "users" })
        {
            var (repos, status) = await FetchReposFromAsync(http, owner, account);
            if (repos is not null) return repos;
            if (status != System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException($"GET /{owner}/{account}/repos -> {(int)status} {status}");
        }
        throw new InvalidOperationException($"No GitHub org or user named '{account}'.");
    }

    private static async Task<(List<GhRepo>? Repos, System.Net.HttpStatusCode Status)> FetchReposFromAsync(
        HttpClient http, string ownerKind, string account)
    {
        var all = new List<GhRepo>();
        for (var page = 1; page <= 10; page++)   // 1000 repos is far beyond any real account here
        {
            var url = $"https://api.github.com/{ownerKind}/{account}/repos?per_page=100&page={page}";
            using var res = await http.GetAsync(url);
            if (!res.IsSuccessStatusCode) return (null, res.StatusCode);

            var batch = JsonSerializer.Deserialize<List<GhRepo>>(await res.Content.ReadAsStringAsync()) ?? [];
            all.AddRange(batch);
            if (batch.Count < 100) break;
        }
        return (all, System.Net.HttpStatusCode.OK);
    }

    /// <summary>
    /// README text for a repo. A local clone wins when one is available — it is faster, works offline, and
    /// reflects uncommitted work in progress; otherwise the canonical copy comes from the GitHub API.
    /// </summary>
    private static async Task<string?> ResolveReadmeAsync(HttpClient http, string org, string repo, string? localRoot)
    {
        if (!string.IsNullOrWhiteSpace(localRoot))
        {
            var dir = Path.Combine(localRoot, repo);
            if (Directory.Exists(dir))
            {
                // Windows is case-insensitive, but this also has to hold on a Linux App Service.
                var match = Directory.EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), "README", StringComparison.OrdinalIgnoreCase));
                if (match is not null) return await File.ReadAllTextAsync(match);
            }
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{org}/{repo}/readme");
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw"));
        using var res = await http.SendAsync(req);
        return res.IsSuccessStatusCode ? await res.Content.ReadAsStringAsync() : null;
    }

    // ---- CMS writes ----

    private static async Task<CmsPage?> EnsureProjectsParentAsync(CmsDbContext db, int? siteId, bool dryRun)
    {
        var parent = await db.Pages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.SiteId == siteId && p.Slug == ProjectsSlug);
        if (parent is not null)
        {
            if (!dryRun)
            {
                parent.Enabled = true;
                parent.IsPublished = true;
                parent.ThemeKey ??= DefaultThemeKey;
                if (parent.ActivePluginsJson is null || IsStockPluginSelection(parent.ActivePluginsJson))
                    parent.ActivePluginsJson = JsonSerializer.Serialize(DefaultPlugins);
                // Adopt the index body only while the page is still the seeder's placeholder heading —
                // an author who has written a real index keeps it.
                if (string.IsNullOrWhiteSpace(parent.BodyHtml)
                    || parent.BodyHtml.Trim() is "<h1>Projects</h1>" or "<h1>MindAttic Projects</h1>")
                    parent.BodyHtml = ProjectsIndexBody;
                await db.SaveChangesAsync();
            }
            return parent;
        }

        if (dryRun) { Console.WriteLine($"[seed-repos] [DRY] create /{ProjectsSlug}"); return null; }

        var now = DateTime.UtcNow;
        parent = new CmsPage
        {
            SiteId = siteId,
            Slug = ProjectsSlug,
            Title = "Projects",
            Kind = PageKind.Data,
            BodyHtml = ProjectsIndexBody,
            BodyTrust = ContentTrust.Author,
            ThemeKey = DefaultThemeKey,
            ActivePluginsJson = JsonSerializer.Serialize(DefaultPlugins),
            IsPublished = true,
            Enabled = true,
            CreatedUtc = now,
            ModifiedUtc = now,
        };
        db.Pages.Add(parent);
        await db.SaveChangesAsync();
        Console.WriteLine($"[seed-repos]   + /{ProjectsSlug}");
        return parent;
    }

    /// <summary>Map PageUid -> the repo name recorded on a previous run, so renames follow the page.</summary>
    private static async Task<Dictionary<Guid, string>> RepoNameByPageUidAsync(CmsDbContext db)
    {
        var rows = await db.ComponentMetadata.Where(m => m.ComponentKey == MetaKeyRepo).ToListAsync();
        var map = new Dictionary<Guid, string>();
        foreach (var row in rows)
        {
            try
            {
                using var doc = JsonDocument.Parse(row.MetadataJson);
                if (doc.RootElement.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name)
                    map[row.PageUid] = name;
            }
            catch { /* a hand-edited metadata blob must not break a sync */ }
        }
        return map;
    }

    /// <summary>
    /// Slugs minted by the retired hand-maintained seeder, mapped to the repo they meant. Without this, the
    /// first GitHub-driven sync would orphan every one of these URLs and mint a parallel page beside it.
    /// One-time migration data — a repo added from here on never needs an entry.
    /// </summary>
    private static readonly Dictionary<string, string> LegacySlugAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["projects/ideas"] = "IdeaCms",
        ["projects/vault"] = "MindAttic.Vault",
        ["projects/legion"] = "MindAttic.Legion",
        ["projects/psst"] = "MindAttic.Psst",
        ["projects/helpers"] = "MindAttic.Helpers",
        ["projects/launcher"] = "MindAttic.Launcher",
        ["projects/mobile"] = "MindAttic.Mobile",
        ["projects/uiux"] = "MindAttic.UiUx",
        ["projects/deploy"] = "MindAttic.Deploy",
        ["projects/authentication"] = "MindAttic.Authentication",
        ["projects/taxrate"] = "TaxRateCollector",
        ["projects/streetsamurai"] = "Prose",          // the repo was renamed StreetSamurai -> Prose
    };

    private static CmsPage? FindExistingPage(
        List<CmsPage> existing, Dictionary<Guid, string> repoNameByPageUid, string repoName, string slug)
    {
        // Identity first: the repo this page was generated from, wherever it now lives.
        var byRepo = existing.FirstOrDefault(p =>
            repoNameByPageUid.TryGetValue(p.Uid, out var n) && string.Equals(n, repoName, StringComparison.OrdinalIgnoreCase));
        if (byRepo is not null) return byRepo;

        // Then the canonical slug.
        var bySlug = existing.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));
        if (bySlug is not null) return bySlug;

        // Finally a legacy slug, so the old URL is carried into history and redirects instead of dying.
        return existing.FirstOrDefault(p =>
            LegacySlugAliases.TryGetValue(p.Slug, out var legacyRepo)
            && string.Equals(legacyRepo, repoName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Minimal markdown for a repo that has no README, so its page still carries real information.</summary>
    private static string StubReadme(GhRepo repo, string org)
    {
        var url = repo.HtmlUrl ?? $"https://github.com/{org}/{repo.Name}";
        var lines = new List<string> { $"# {TitleFor(repo.Name)}", "" };
        lines.Add(string.IsNullOrWhiteSpace(repo.Description)
            ? "_This repository does not have a README yet._"
            : repo.Description);
        lines.Add("");
        if (!string.IsNullOrWhiteSpace(repo.Language)) lines.Add($"**Language:** {repo.Language}  ");
        if (!string.IsNullOrWhiteSpace(repo.Homepage)) lines.Add($"**Site:** <{repo.Homepage}>  ");
        lines.Add($"**Source:** <{url}>");
        return string.Join('\n', lines);
    }

    /// <summary>True when a page's plugin selection is still verbatim seeder output.</summary>
    private static bool IsStockPluginSelection(string json)
    {
        try
        {
            var current = JsonSerializer.Deserialize<string[]>(json) ?? [];
            return StockPluginSets.Any(stock => stock.SequenceEqual(current, StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            return false;   // unparseable = hand-edited; leave it alone
        }
    }

    private static async Task RecordSlugHistoryAsync(CmsDbContext db, int pageId, string oldSlug)
    {
        oldSlug = oldSlug.Trim('/').ToLowerInvariant();
        if (oldSlug.Length == 0) return;
        var already = await db.PageSlugHistory.AnyAsync(h => h.PageId == pageId && h.OldSlug == oldSlug);
        if (already) return;
        db.PageSlugHistory.Add(new PageSlugHistory
        {
            PageId = pageId,
            OldSlug = oldSlug,
            IsVanity = false,
            CreatedUtc = DateTime.UtcNow,
        });
    }

    private static async Task UpsertMetadataAsync(CmsDbContext db, Guid pageUid, string componentKey, string json)
    {
        var row = await db.ComponentMetadata
            .FirstOrDefaultAsync(m => m.PageUid == pageUid && m.ComponentKey == componentKey && m.SlotName == "main");
        var now = DateTime.UtcNow;
        if (row is null)
        {
            db.ComponentMetadata.Add(new ComponentMetadata
            {
                PageUid = pageUid, ComponentKey = componentKey, SlotName = "main",
                MetadataJson = json, CreatedUtc = now, ModifiedUtc = now,
            });
        }
        else
        {
            row.MetadataJson = json;
            row.ModifiedUtc = now;
        }
        await db.SaveChangesAsync();
    }

    // ---- naming ----

    /// <summary>
    /// Repo name -> URL slug: lowercase, with '.', '_' and whitespace folded to '-'.
    /// <c>MindAttic.Vault</c> -> <c>mindattic-vault</c>, <c>mindattic.com</c> -> <c>mindattic-com</c>.
    /// </summary>
    internal static string SlugFor(string repoName)
    {
        var chars = repoName.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    /// <summary>Repo name -> page title. <c>MindAttic.Vault</c> -> <c>MindAttic Vault</c>; lowercase
    /// domain-style names (<c>mindattic.com</c>) keep their dots because that IS the name.</summary>
    internal static string TitleFor(string repoName) =>
        repoName.Contains('.') && repoName == repoName.ToLowerInvariant()
            ? repoName
            : repoName.Replace('.', ' ').Replace('_', ' ');

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
