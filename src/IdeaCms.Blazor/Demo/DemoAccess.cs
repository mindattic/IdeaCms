using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.RateLimiting;
using MindAttic.Ideas.Abstractions;

namespace IdeaCms.Blazor.Demo;

/// <summary>
/// The public demo of this CMS, as seen from the site that advertises it. The demo is a separate
/// deployment (M.IDEA-LAW-11); its operator (the hourly reset workflow) publishes the current login as one
/// Key Vault secret, and this host only ever reads it. Nothing here is configured on the demo itself.
/// </summary>
public sealed class DemoOptions
{
    public string? Url { get; set; }
    public string? KeyVaultUri { get; set; }
    public string CredentialsSecretName { get; set; } = "credentials";
    public string? TurnstileSiteKey { get; set; }
    public string? TurnstileSecretKey { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(KeyVaultUri);
    public bool RevealEnabled => Enabled && !string.IsNullOrWhiteSpace(TurnstileSiteKey) && !string.IsNullOrWhiteSpace(TurnstileSecretKey);
}

/// <summary>The JSON the reset workflow writes: <c>{status, url, username, password, validUntilUtc}</c>.</summary>
public sealed record DemoCredentials(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("password")] string? Password,
    [property: JsonPropertyName("validUntilUtc")] DateTimeOffset? ValidUntilUtc)
{
    public bool Ready => Status == "ready" && !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password);
}

public interface IDemoCredentialsSource
{
    /// <summary>The current published login, or null if it cannot be read. Never throws.</summary>
    Task<DemoCredentials?> GetAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads the credentials secret directly (an app-setting Key Vault reference is cached for hours, far too
/// long for a login that rotates hourly), with a short cache so a page view never waits on Key Vault.
/// </summary>
public sealed class KeyVaultDemoCredentialsSource(SecretClient client, DemoOptions options, ILogger<KeyVaultDemoCredentialsSource> log)
    : IDemoCredentialsSource
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DemoCredentials? Value, DateTimeOffset At) _cache;

    public async Task<DemoCredentials?> GetAsync(CancellationToken ct = default)
    {
        if (DateTimeOffset.UtcNow - _cache.At < CacheFor) return _cache.Value;
        await _gate.WaitAsync(ct);
        try
        {
            if (DateTimeOffset.UtcNow - _cache.At < CacheFor) return _cache.Value;
            DemoCredentials? value = null;
            try
            {
                var secret = await client.GetSecretAsync(options.CredentialsSecretName, cancellationToken: ct);
                value = JsonSerializer.Deserialize<DemoCredentials>(secret.Value.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning("Demo credentials unavailable: {Error}", ex.Message);
            }
            _cache = (value, DateTimeOffset.UtcNow);
            return value;
        }
        finally { _gate.Release(); }
    }
}

public interface ITurnstileVerifier
{
    /// <summary>True only when Cloudflare confirms the token was issued for <paramref name="action"/>.</summary>
    Task<bool> VerifyAsync(string token, string? remoteIp, string action, CancellationToken ct = default);
}

public sealed class CloudflareTurnstileVerifier(HttpClient http, DemoOptions options, ILogger<CloudflareTurnstileVerifier> log)
    : ITurnstileVerifier
{
    private static readonly Uri SiteVerify = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    private sealed record Result(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("action")] string? Action);

    public async Task<bool> VerifyAsync(string token, string? remoteIp, string action, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string> { ["secret"] = options.TurnstileSecretKey ?? "", ["response"] = token };
        if (!string.IsNullOrEmpty(remoteIp)) form["remoteip"] = remoteIp;
        try
        {
            using var response = await http.PostAsync(SiteVerify, new FormUrlEncodedContent(form), ct);
            var result = await response.Content.ReadFromJsonAsync<Result>(ct);
            return result is { Success: true } && result.Action == action;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("Turnstile verification failed closed: {Error}", ex.Message);
            return false;
        }
    }
}

public sealed class DemoAccessService(DemoOptions options, IDemoCredentialsSource source) : IDemoAccess
{
    public async Task<DemoInfo?> GetInfoAsync(CancellationToken ct = default)
    {
        if (!options.Enabled) return null;
        var current = await source.GetAsync(ct);
        return new DemoInfo(options.Url!, current?.Ready == true, current?.ValidUntilUtc,
            options.RevealEnabled ? options.TurnstileSiteKey : null);
    }
}

public sealed record DemoRevealRequest([property: JsonPropertyName("token")] string? Token);

public static class DemoReveal
{
    public const string Action = "demo-reveal";
    public const string RateLimitPolicy = "demo-reveal";

    /// <summary>
    /// Returns the current demo login to a browser that passed Turnstile. Fails closed at every step:
    /// no reveal configured → 503; no token → 400; Cloudflare says no → 403; demo resetting → 503.
    /// </summary>
    public static async Task<IResult> HandleAsync(
        DemoRevealRequest? request, string? remoteIp, DemoOptions options,
        ITurnstileVerifier verifier, IDemoCredentialsSource source, CancellationToken ct = default)
    {
        if (!options.RevealEnabled)
            return Results.Json(new { error = "The demo login is not available." }, statusCode: StatusCodes.Status503ServiceUnavailable);
        if (string.IsNullOrWhiteSpace(request?.Token) || request.Token.Length > 4096)
            return Results.Json(new { error = "Complete the check first." }, statusCode: StatusCodes.Status400BadRequest);
        if (!await verifier.VerifyAsync(request.Token, remoteIp, Action, ct))
            return Results.Json(new { error = "The check did not pass. Try again." }, statusCode: StatusCodes.Status403Forbidden);

        var current = await source.GetAsync(ct);
        if (current is not { Ready: true })
            return Results.Json(new { error = "The demo is resetting. Try again in a few minutes." }, statusCode: StatusCodes.Status503ServiceUnavailable);

        return Results.Json(new
        {
            url = current.Url ?? options.Url,
            username = current.Username,
            password = current.Password,
            validUntilUtc = current.ValidUntilUtc,
        });
    }

    public static IServiceCollection AddDemoAccess(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Demo").Get<DemoOptions>() ?? new DemoOptions();
        services.AddSingleton(options);
        if (!options.Enabled) return services;

        services.AddSingleton(_ => new SecretClient(new Uri(options.KeyVaultUri!), new Azure.Identity.DefaultAzureCredential()));
        services.AddSingleton<IDemoCredentialsSource, KeyVaultDemoCredentialsSource>();
        services.AddHttpClient<ITurnstileVerifier, CloudflareTurnstileVerifier>(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<IDemoAccess, DemoAccessService>();

        // Per-client-IP budget on the reveal. The client IP is the one UseForwardedHeaders resolved: the
        // right-most X-Forwarded-For entry (ForwardLimit 1), which App Service's front end appends, so a
        // client cannot spoof its way into a fresh budget.
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }

    public static void MapDemoReveal(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<DemoOptions>();
        if (!options.Enabled) return;

        app.UseRateLimiter();
        app.MapPost("/_demo/reveal", async (DemoRevealRequest? request, HttpContext http,
                ITurnstileVerifier verifier, IDemoCredentialsSource source, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                http.Response.Headers["X-Robots-Tag"] = "noindex";
                return await HandleAsync(request, http.Connection.RemoteIpAddress?.ToString(), options, verifier, source, ct);
            })
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimitPolicy);
    }
}
