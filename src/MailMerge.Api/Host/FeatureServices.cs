using MailMerge.Api.Features.Accounts;
using MailMerge.Api.Features.Accounts.Microsoft365;
using MailMerge.Api.Features.Accounts.Sending;
using MailMerge.Api.Features.Campaigns.Delivery;
using MailMerge.Api.Features.OptOuts;

namespace MailMerge.Api.Host;

/// <summary>Services owned by features beyond their handlers (adapters, background workers).</summary>
internal static class FeatureServices
{
    public static IServiceCollection AddMailMergeFeatures(this IServiceCollection services)
    {
        services.AddScoped<OptOutRegistry>();
        services.AddScoped<DeliveryPlanner>();

        services.AddOptions<MicrosoftSettings>().BindConfiguration(MicrosoftSettings.SectionName);
        services.AddSingleton<MicrosoftClientFactory>();
        services.AddSingleton<MicrosoftSignInFlow>();
        services.AddSingleton<IMicrosoftTokenProvider, MsalTokenProvider>();
        services.AddHttpClient(GraphMailSender.HttpClientName, c => c.Timeout = TimeSpan.FromMinutes(5));

        services.AddSingleton<AccountSecretsProtector>();
        services.AddSingleton<TestMailbox>();
        services.AddSingleton<IMailSenderFactory, MailSenderFactory>();

        return services;
    }
}
