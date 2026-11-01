using Microsoft.AspNetCore.Http;
using IdeaCms.Blazor;

namespace IdeaCms.Tests;

/// <summary>
/// M.IDEA-§4.14 startup. On the shared B1 plan an Entra SQL login during a cold start was reset mid-handshake;
/// thrown from top-level statements it aborted the runtime (SIGABRT, exit 134) and App Service restarted the
/// container into the same contention. These pin the replacement: the server listens first, transient
/// failures are retried, permanent ones surface at once, and only /_health is served until ready.
/// </summary>
[TestFixture]
public class StartupGateTests
{
    private static readonly StartupRetryPolicy Fast = new(4, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Test]
    public async Task ATransientFailureIsRetriedUntilInitialisationSucceeds()
    {
        var calls = 0;
        var log = new StringWriter();

        await StartupGate.RunWithRetryAsync(_ =>
        {
            calls++;
            if (calls < 3) throw new TimeoutException("login timed out");
            return Task.CompletedTask;
        }, Fast, log, NoDelay);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(3));
            Assert.That(log.ToString(), Does.Contain("attempt 1/4").And.Contain("attempt 2/4"));
        });
    }

    [Test]
    public void APermanentFailureIsNotRetried()
    {
        var calls = 0;

        Assert.ThrowsAsync<InvalidOperationException>(() => StartupGate.RunWithRetryAsync(_ =>
        {
            calls++;
            throw new InvalidOperationException("DataProtection:BlobUri is required in production.");
        }, Fast, TextWriter.Null, NoDelay));

        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void TheLastTransientFailureSurfacesOnceTheAttemptsAreSpent()
    {
        var calls = 0;

        Assert.ThrowsAsync<IOException>(() => StartupGate.RunWithRetryAsync(_ =>
        {
            calls++;
            throw new IOException("Connection reset by peer");
        }, Fast, TextWriter.Null, NoDelay));

        Assert.That(calls, Is.EqualTo(Fast.MaxAttempts));
    }

    [Test]
    public void ATransientCauseIsFoundAnywhereInTheChain()
    {
        // EF and SqlClient wrap the socket error that actually happened (DbUpdateException ->
        // SqlException -> IOException), and the outermost type alone says nothing about it.
        var wrapped = new InvalidOperationException("save failed",
            new Exception("login failed", new IOException("Unable to write data to the transport connection")));

        Assert.Multiple(() =>
        {
            Assert.That(StartupGate.IsTransient(wrapped), Is.True);
            Assert.That(StartupGate.IsTransient(new AggregateException(new TimeoutException())), Is.True);
            Assert.That(StartupGate.IsTransient(new InvalidOperationException("bad .idealist")), Is.False);
        });
    }

    [Test]
    public void TheBackoffDoublesAndIsCapped()
    {
        var policy = StartupRetryPolicy.Default;

        Assert.Multiple(() =>
        {
            Assert.That(policy.DelayAfter(1), Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(policy.DelayAfter(2), Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(policy.DelayAfter(5), Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(policy.DelayAfter(7), Is.EqualTo(TimeSpan.FromSeconds(60)));
        });
    }

    [TestCase("/", 503)]
    [TestCase("/admin", 503)]
    [TestCase("/_health", 200)]
    public async Task BeforeReadinessOnlyHealthIsServed(string path, int expected)
    {
        var readiness = new StartupReadiness();
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;

        await StartupGate.Gate(readiness, c => { c.Response.StatusCode = 200; return Task.CompletedTask; })(ctx);

        Assert.That(ctx.Response.StatusCode, Is.EqualTo(expected));
        if (expected == 503)
            Assert.That(ctx.Response.Headers.RetryAfter.ToString(), Is.EqualTo("10"));
    }

    [Test]
    public async Task OnceReadyEveryRequestPassesThrough()
    {
        var readiness = new StartupReadiness();
        readiness.MarkReady();
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/";

        await StartupGate.Gate(readiness, c => { c.Response.StatusCode = 200; return Task.CompletedTask; })(ctx);

        Assert.That(ctx.Response.StatusCode, Is.EqualTo(200));
    }

    [Test]
    public void HealthReportsReadinessAndBuild()
    {
        var readiness = new StartupReadiness();
        var ctx = new DefaultHttpContext();

        StartupGate.Health(ctx, readiness);
        var before = ctx.Response.Headers["X-IdeaCms-Ready"].ToString();
        readiness.MarkReady();
        StartupGate.Health(ctx, readiness);

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo("false"));
            Assert.That(ctx.Response.Headers["X-IdeaCms-Ready"].ToString(), Is.EqualTo("true"));
            Assert.That(ctx.Response.Headers["X-IdeaCms-Version"].ToString(), Is.Not.Empty);
        });
    }
}
