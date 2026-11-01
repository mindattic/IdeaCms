using System.Data.Common;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace IdeaCms.Blazor;

/// <summary>
/// Whether the boot-time initialisation (discovery, seed, admin bootstrap, provisioning) has finished.
/// The server listens before that work runs, so <c>/_health</c> answers within seconds of the process
/// starting; every other request gets <c>503 starting</c> until <see cref="MarkReady"/> (M.IDEA-§4.14).
/// </summary>
public sealed class StartupReadiness
{
    private volatile bool _ready;

    public bool IsReady => _ready;

    public void MarkReady() => _ready = true;
}

/// <summary>How many times, and how far apart, the boot initialisation is retried on a transient failure.</summary>
public sealed record StartupRetryPolicy(int MaxAttempts, TimeSpan InitialDelay, TimeSpan MaxDelay)
{
    /// <summary>8 attempts, 5 s doubling to a 60 s cap: about four minutes of waiting in all.</summary>
    public static StartupRetryPolicy Default { get; } = new(8, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));

    /// <summary>The wait after failed attempt <paramref name="failedAttempt"/> (1-based).</summary>
    public TimeSpan DelayAfter(int failedAttempt)
    {
        var ticks = InitialDelay.Ticks * Math.Pow(2, Math.Max(0, failedAttempt - 1));
        return ticks >= MaxDelay.Ticks ? MaxDelay : TimeSpan.FromTicks((long)ticks);
    }
}

/// <summary>
/// The boot sequence's failure handling. On the shared B1 plan a cold start competes with the other
/// site's for one core, and an Entra-authenticated SQL login can be reset mid-handshake while the
/// managed-identity token is still being fetched. Thrown from top-level statements, that exception was
/// unhandled: the runtime aborted (SIGABRT, exit 134), wrote a core dump, and App Service restarted the
/// container into the same contention. Now a transient failure is retried, and a permanent one ends the
/// process with exit code 1 instead of an abort.
/// </summary>
public static class StartupGate
{
    /// <summary>
    /// True when any exception in the chain is a connectivity or identity failure that a later attempt
    /// can succeed past: a database error, a timeout, a socket or IO error, or an Azure SDK / credential
    /// failure. Configuration and validation errors (a missing setting, a bad <c>.idealist</c>) are not
    /// transient and fail on the first attempt.
    /// </summary>
    public static bool IsTransient(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is AggregateException agg && agg.InnerExceptions.Any(IsTransient)) return true;
            if (e is DbException or TimeoutException or IOException or SocketException
                or Azure.RequestFailedException
                or Azure.Identity.AuthenticationFailedException
                or Azure.Identity.CredentialUnavailableException)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Runs <paramref name="init"/>, retrying transient failures per <paramref name="policy"/>. Rethrows a
    /// non-transient failure at once, and the last failure once the attempts are spent.
    /// <paramref name="init"/> must be idempotent; every boot step is (discovery, seed, admin bootstrap and
    /// both provisioning paths all reconcile rather than insert blindly).
    /// </summary>
    public static async Task RunWithRetryAsync(
        Func<CancellationToken, Task> init, StartupRetryPolicy policy, TextWriter log,
        Func<TimeSpan, CancellationToken, Task>? delay = null, CancellationToken ct = default)
    {
        delay ??= Task.Delay;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await init(ct);
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < policy.MaxAttempts && IsTransient(ex))
            {
                var wait = policy.DelayAfter(attempt);
                var reason = ex.Message.Split('\n')[0].Trim();
                log.WriteLine($"[startup] attempt {attempt}/{policy.MaxAttempts} failed with a transient " +
                              $"{ex.GetType().Name}: {reason} Retrying in {wait.TotalSeconds:0}s.");
                await delay(wait, ct);
            }
        }
    }

    /// <summary>
    /// Middleware: until <paramref name="readiness"/> is ready, everything except <c>/_health</c> is
    /// answered <c>503</c> with <c>Retry-After</c>, so a visitor, the deploy smoke test and the demo reset
    /// all see "not yet" rather than a half-initialised site.
    /// </summary>
    public static RequestDelegate Gate(StartupReadiness readiness, RequestDelegate next) => async ctx =>
    {
        if (readiness.IsReady || ctx.Request.Path.StartsWithSegments(HealthPath))
        {
            await next(ctx);
            return;
        }
        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        ctx.Response.Headers.RetryAfter = "10";
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.ContentType = "text/plain";
        await ctx.Response.WriteAsync("starting");
    };

    public const string HealthPath = "/_health";

    /// <summary>
    /// <c>/_health</c>: liveness, 200 as soon as the server listens and never touching the database.
    /// Headers say whether initialisation has finished (<c>X-IdeaCms-Ready</c>) and which build is running
    /// (<c>X-IdeaCms-Version</c>, the informational version carrying the commit), so the deploy can tell the
    /// new container from the one it replaced.
    /// </summary>
    public static IResult Health(HttpContext ctx, StartupReadiness readiness)
    {
        ctx.Response.Headers["X-IdeaCms-Ready"] = readiness.IsReady ? "true" : "false";
        ctx.Response.Headers["X-IdeaCms-Version"] = BuildVersion;
        ctx.Response.Headers.CacheControl = "no-store";
        return Results.Text("healthy", "text/plain");
    }

    public static string BuildVersion { get; } =
        typeof(StartupGate).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}
