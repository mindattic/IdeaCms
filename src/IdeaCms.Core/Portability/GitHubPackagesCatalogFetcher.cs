using System.Net.Http.Headers;
using System.Text.Json;

namespace IdeaCms.Core.Portability;

/// <summary>
/// The real <see cref="IPackageCatalogFetcher"/> — talks to GitHub's own REST API (<c>GET
/// /orgs/{org}/packages?package_type=nuget</c> to enumerate package ids, <c>GET
/// /orgs/{org}/packages/nuget/{id}/versions</c> to enumerate versions per id), NOT the NuGet v3
/// protocol's search resource, which GitHub Packages does not support reliably. NOT exercised by the
/// automated test suite — verify this by hand against the real org before depending on it in
/// production (pagination, auth header, that this org is not secretly a GitHub *user* account, whose
/// packages live under <c>/users/{login}/packages</c> instead — this org-only shape is what
/// <c>publish-nuget.ps1</c>'s feed already assumes).
/// </summary>
public sealed class GitHubPackagesCatalogFetcher(string? org, string? patToken) : IPackageCatalogFetcher
{
    private const int PageSize = 100;
    private const int MaxPages = 50; // 5,000 items — a generous ceiling against a runaway paginate loop.

    private static readonly HttpClient Http = new() { BaseAddress = new Uri("https://api.github.com") };

    public bool IsConfigured => !string.IsNullOrWhiteSpace(org);

    public async Task<IReadOnlyList<string>> ListPackageIdsAsync(CancellationToken ct = default)
    {
        var ids = new List<string>();
        await foreach (var el in PaginateAsync($"/orgs/{org}/packages?package_type=nuget", ct))
            if (el.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } s)
                ids.Add(s);
        return ids;
    }

    public async Task<IReadOnlyList<string>> ListVersionsAsync(string packageId, CancellationToken ct = default)
    {
        var versions = new List<string>();
        var encoded = Uri.EscapeDataString(packageId);
        await foreach (var el in PaginateAsync($"/orgs/{org}/packages/nuget/{encoded}/versions", ct))
            if (el.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } s)
                versions.Add(s);
        return versions;
    }

    private async IAsyncEnumerable<JsonElement> PaginateAsync(
        string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var separator = path.Contains('?') ? '&' : '?';
        for (var page = 1; page <= MaxPages; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{path}{separator}per_page={PageSize}&page={page}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("IdeaCms", "1"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            if (!string.IsNullOrEmpty(patToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patToken);

            using var response = await Http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var count = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                yield return el.Clone();
                count++;
            }
            if (count < PageSize) yield break; // last page
        }
    }
}
