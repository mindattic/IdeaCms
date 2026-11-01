using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using IdeaCms.Blazor.Services;
using IdeaCms.Core.Data;
using MindAttic.Media;
using MindAttic.Media.Azure;

namespace IdeaCms.Tests;

/// <summary>
/// M.IDEA-§4.11: the media backing store is chosen by configuration, and <c>/_media/{uid}</c> is the
/// page-facing contract either way. These assert the selection itself — no Azure account is touched,
/// only the service descriptors the selection produces.
/// </summary>
[TestFixture]
public class MediaProviderSetupTests
{
    private const string MediaRoot = @"C:\ideas\media";

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static IServiceCollection Baseline()
    {
        // Stand in for what AddIdeaCms leaves behind: the local store already registered.
        var services = new ServiceCollection();
        services.AddMedia<CmsDbContext>();
        return services;
    }

    private static Type? StoreType(IServiceCollection services) =>
        services.Single(d => d.ServiceType == typeof(IMediaStore)).ImplementationType;

    [Test]
    public void NoConfiguration_KeepsTheLocalDiskStore()
    {
        var services = Baseline();

        services.AddConfiguredMediaStore<CmsDbContext>(Config(), MediaRoot);

        Assert.Multiple(() =>
        {
            Assert.That(StoreType(services), Is.EqualTo(typeof(LocalDiskMediaStore<CmsDbContext>)));
            Assert.That(services.Any(d => d.ServiceType == typeof(IMediaUrlSigner)), Is.False,
                "with no signer the endpoint must stream, not redirect");
        });

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<MediaStoreOptions>>().Value;
        Assert.That(options.MediaRoot, Is.EqualTo(MediaRoot));
    }

    [Test]
    public void ProviderAzure_ReplacesTheStoreAndRegistersASigner()
    {
        var services = Baseline();

        services.AddConfiguredMediaStore<CmsDbContext>(
            Config(("Media:Provider", "azure"),
                   ("Media:Azure:ConnectionString", "UseDevelopmentStorage=true"),
                   ("Media:Azure:ContainerName", "ideas-media")),
            MediaRoot);

        Assert.Multiple(() =>
        {
            Assert.That(StoreType(services), Is.EqualTo(typeof(AzureBlobMediaStore<CmsDbContext>)));
            Assert.That(services.Count(d => d.ServiceType == typeof(IMediaStore)), Is.EqualTo(1),
                "the local store must be replaced, not stacked behind the Azure one");
            Assert.That(services.Single(d => d.ServiceType == typeof(IMediaUrlSigner)).ImplementationType,
                Is.EqualTo(typeof(AzureBlobUrlSigner)));
        });

        var azure = services.BuildServiceProvider().GetRequiredService<IOptions<AzureMediaOptions>>().Value;
        Assert.That(azure.ContainerName, Is.EqualTo("ideas-media"));
    }

    [Test]
    public void ProviderAzure_CarriesSignedUrlLifetimeThroughToTheEndpointOptions()
    {
        var services = Baseline();

        services.AddConfiguredMediaStore<CmsDbContext>(
            Config(("Media:Provider", "azure"),
                   ("Media:Azure:BlobServiceUri", "https://mindattic.blob.core.windows.net"),
                   ("Media:Azure:SignedUrlMinutes", "45")),
            MediaRoot);

        var provider = services.BuildServiceProvider();

        Assert.Multiple(() =>
        {
            Assert.That(provider.GetRequiredService<IOptions<AzureMediaOptions>>().Value.SignedUrlLifetime,
                Is.EqualTo(TimeSpan.FromMinutes(45)));
            Assert.That(provider.GetRequiredService<IOptions<MediaStoreOptions>>().Value.SignedUrlLifetime,
                Is.EqualTo(TimeSpan.FromMinutes(45)),
                "the endpoint reads MediaStoreOptions, so the shared knobs must be mirrored onto it");
        });
    }

    [Test]
    public void ProviderAzure_WithoutCredentials_FailsClosed()
    {
        var services = Baseline();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddConfiguredMediaStore<CmsDbContext>(Config(("Media:Provider", "azure")), MediaRoot));

        Assert.That(ex!.Message, Does.Contain("ConnectionString or Media:Azure:BlobServiceUri"));
    }

    [Test]
    public void UnknownProvider_FailsClosed()
    {
        var services = Baseline();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddConfiguredMediaStore<CmsDbContext>(Config(("Media:Provider", "s3")), MediaRoot));

        Assert.That(ex!.Message, Does.Contain("'local' or 'azure'"));
    }
}
