using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using IdeaCms.Blazor.Demo;

namespace IdeaCms.Tests;

[TestFixture]
public class DemoRevealTests
{
    private sealed class Verifier(bool pass) : ITurnstileVerifier
    {
        public string? SeenAction { get; private set; }
        public int Calls { get; private set; }
        public Task<bool> VerifyAsync(string token, string? remoteIp, string action, CancellationToken ct = default)
        {
            Calls++; SeenAction = action;
            return Task.FromResult(pass);
        }
    }

    private sealed class Source(DemoCredentials? creds) : IDemoCredentialsSource
    {
        public Task<DemoCredentials?> GetAsync(CancellationToken ct = default) => Task.FromResult(creds);
    }

    private static readonly DemoCredentials Ready =
        new("ready", "https://demo.example", "admin", "s3cret-hourly", DateTimeOffset.UtcNow.AddMinutes(40));

    private static DemoOptions Options(bool turnstile = true) => new()
    {
        Url = "https://demo.example", KeyVaultUri = "https://kv.example/",
        TurnstileSiteKey = turnstile ? "site" : null, TurnstileSecretKey = turnstile ? "secret" : null,
    };

    private static int? Status(IResult r) => (r as IStatusCodeHttpResult)?.StatusCode;
    private static string Body(IResult r) => System.Text.Json.JsonSerializer.Serialize((r as IValueHttpResult)?.Value);

    [Test]
    public async Task WithoutTurnstileKeys_TheRevealIsOff_NotOpen()
    {
        var verifier = new Verifier(pass: true);
        var r = await DemoReveal.HandleAsync(new("tok"), "1.2.3.4", Options(turnstile: false), verifier, new Source(Ready));
        Assert.Multiple(() =>
        {
            Assert.That(Status(r), Is.EqualTo(503));
            Assert.That(Body(r), Does.Not.Contain("s3cret"));
            Assert.That(verifier.Calls, Is.Zero);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    public async Task NoToken_Is400(string? token)
    {
        var r = await DemoReveal.HandleAsync(new(token), "1.2.3.4", Options(), new Verifier(true), new Source(Ready));
        Assert.That(Status(r), Is.EqualTo(400));
    }

    [Test]
    public async Task FailedCheck_Is403_AndLeaksNothing()
    {
        var r = await DemoReveal.HandleAsync(new("tok"), "1.2.3.4", Options(), new Verifier(false), new Source(Ready));
        Assert.Multiple(() =>
        {
            Assert.That(Status(r), Is.EqualTo(403));
            Assert.That(Body(r), Does.Not.Contain("s3cret"));
        });
    }

    [Test]
    public async Task WhileResetting_Is503()
    {
        var resetting = new DemoCredentials("resetting", null, null, null, null);
        var r = await DemoReveal.HandleAsync(new("tok"), "1.2.3.4", Options(), new Verifier(true), new Source(resetting));
        Assert.That(Status(r), Is.EqualTo(503));
    }

    [Test]
    public async Task PassedCheck_ReturnsTheLogin_ForTheRevealActionOnly()
    {
        var verifier = new Verifier(true);
        var r = await DemoReveal.HandleAsync(new("tok"), "1.2.3.4", Options(), verifier, new Source(Ready));
        Assert.Multiple(() =>
        {
            Assert.That(Status(r) ?? 200, Is.EqualTo(200));
            Assert.That(Body(r), Does.Contain("s3cret-hourly").And.Contain("admin"));
            Assert.That(verifier.SeenAction, Is.EqualTo(DemoReveal.Action),
                "a token minted for another widget/action on the same site key must not unlock the login");
        });
    }

    [Test]
    public async Task InfoNeverCarriesCredentials()
    {
        var info = await new DemoAccessService(Options(), new Source(Ready)).GetInfoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(info!.Ready, Is.True);
            Assert.That(info.TurnstileSiteKey, Is.EqualTo("site"));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(info), Does.Not.Contain("s3cret"));
        });
    }
}
