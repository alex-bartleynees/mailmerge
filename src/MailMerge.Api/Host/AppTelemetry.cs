using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MailMerge.Api.Host;

public static class AppTelemetry
{
    public const string SourceName = "MailMerge.Api";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> MessagesProcessed =
        Meter.CreateCounter<long>("mailmerge.messages.processed", description: "Messages drafted or sent, by outcome");
}
