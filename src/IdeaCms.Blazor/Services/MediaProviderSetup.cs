using Microsoft.EntityFrameworkCore;
using MindAttic.Media;
using MindAttic.Media.Azure;

namespace IdeaCms.Blazor.Services;

/// <summary>
/// Chooses the media backing store from configuration (M.IDEA-§4.11). The page-facing contract is
/// <c>/_media/{uid}</c> whichever provider wins, so switching stores never touches page markup.
/// </summary>
public static class MediaProviderSetup
{
    public const string DefaultProvider = "local";

    /// <summary>
    /// Replaces the local store registered by <c>AddIdeaCms</c> when <c>Media:Provider=azure</c>.
    /// Fails closed on an unknown provider or on Azure without credentials rather than silently
    /// falling back to disk — a deployment that thinks it is on blob storage and is not would lose
    /// every upload on the next app-service restart.
    /// </summary>
    public static IServiceCollection AddConfiguredMediaStore<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string mediaRoot)
        where TContext : DbContext
    {
        var provider = configuration["Media:Provider"] ?? DefaultProvider;

        if (string.Equals(provider, "azure", StringComparison.OrdinalIgnoreCase))
            return AddAzure<TContext>(services, configuration.GetSection("Media:Azure"), mediaRoot);

        if (string.Equals(provider, DefaultProvider, StringComparison.OrdinalIgnoreCase))
        {
            services.Configure<MediaStoreOptions>(o => o.MediaRoot = mediaRoot);
            return services;
        }

        throw new InvalidOperationException(
            $"Media:Provider must be 'local' or 'azure', not '{provider}'.");
    }

    static IServiceCollection AddAzure<TContext>(
        IServiceCollection services,
        IConfigurationSection azure,
        string mediaRoot)
        where TContext : DbContext
    {
        var connectionString = azure["ConnectionString"];
        var blobServiceUri = Uri.TryCreate(azure["BlobServiceUri"], UriKind.Absolute, out var svc) ? svc : null;

        if (string.IsNullOrWhiteSpace(connectionString) && blobServiceUri is null)
        {
            throw new InvalidOperationException(
                "Media:Provider=azure requires Media:Azure:ConnectionString or Media:Azure:BlobServiceUri " +
                "(credentials belong in the Vault 'Media' bucket, HOUSE-LAW-3).");
        }

        var publicBaseUri = Uri.TryCreate(azure["PublicBaseUri"], UriKind.Absolute, out var cdn) ? cdn : null;
        var signedUrlMinutes = int.TryParse(azure["SignedUrlMinutes"], out var minutes) && minutes > 0
            ? minutes
            : (int?)null;

        return services.AddMediaAzure<TContext>(o =>
        {
            o.MediaRoot = mediaRoot;
            o.ConnectionString = connectionString;
            o.BlobServiceUri = blobServiceUri;
            o.ContainerName = azure["ContainerName"] ?? "media";
            o.PublicRead = azure.GetValue("PublicRead", false);
            o.PublicBaseUri = publicBaseUri;
            if (signedUrlMinutes is int m)
                o.SignedUrlLifetime = TimeSpan.FromMinutes(m);
        });
    }
}
