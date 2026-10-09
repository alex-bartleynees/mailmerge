namespace MailMerge.Api.Host;

/// <summary>Services owned by features beyond their handlers (adapters, background workers).</summary>
internal static class FeatureServices
{
    public static IServiceCollection AddMailMergeFeatures(this IServiceCollection services)
    {
        services.AddScoped<Features.OptOuts.OptOutRegistry>();

        return services;
    }
}
