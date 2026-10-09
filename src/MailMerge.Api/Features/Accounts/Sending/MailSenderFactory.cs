using MailMerge.Api.Features.Accounts.Microsoft365;

namespace MailMerge.Api.Features.Accounts.Sending;

public interface IMailSenderFactory
{
    IMailSender Create(SendingAccount account);
}

internal sealed class MailSenderFactory(
    IHttpClientFactory http,
    IMicrosoftTokenProvider microsoftTokens,
    AccountSecretsProtector secrets,
    TestMailbox testMailbox,
    TimeProvider clock,
    ILoggerFactory logging,
    Microsoft.Extensions.Options.IOptions<MicrosoftSettings> microsoft) : IMailSenderFactory
{
    public IMailSender Create(SendingAccount account) => account.Kind switch
    {
        AccountKind.Microsoft => new GraphMailSender(account, http.CreateClient(GraphMailSender.HttpClientName),
            microsoftTokens, microsoft.Value, clock, logging.CreateLogger<GraphMailSender>()),
        AccountKind.Smtp => new SmtpMailSender(account, secrets.Unprotect(account), clock,
            logging.CreateLogger<SmtpMailSender>()),
        AccountKind.Test => new TestMailSender(account, testMailbox, clock),
        _ => throw new MessageRejectedException($"Unknown account type: {account.Kind}"),
    };
}
