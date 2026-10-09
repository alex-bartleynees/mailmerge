namespace MailMerge.Api.Features.Campaigns.Delivery;

public static class Pacing
{
    /// <summary>
    /// When an account may send again: 60/perMinute seconds later, ±30% jitter so the rhythm doesn't look robotic.
    /// <paramref name="random"/> is in [0, 1).
    /// </summary>
    public static DateTimeOffset NextSendAt(DateTimeOffset now, int perMinute, double random)
    {
        var rate = Math.Clamp(perMinute, 1, 600);
        var seconds = 60.0 / rate * (0.7 + 0.6 * random);
        return now + TimeSpan.FromSeconds(seconds);
    }
}
