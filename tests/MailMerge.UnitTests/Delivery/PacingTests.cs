using MailMerge.Api.Features.Campaigns.Delivery;

namespace MailMerge.UnitTests.Delivery;

public sealed class PacingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(20, 0.0, 2.1)]
    [InlineData(20, 0.5, 3.0)]
    [InlineData(20, 0.999999, 3.9)]
    [InlineData(60, 0.5, 1.0)]
    public void Spaces_sends_at_the_rate_with_thirty_percent_jitter(int perMinute, double random, double seconds) =>
        (Pacing.NextSendAt(Now, perMinute, random) - Now).TotalSeconds.Should().BeApproximately(seconds, 0.001);

    [Fact]
    public void Nonsense_rates_are_clamped()
    {
        (Pacing.NextSendAt(Now, 0, 0.5) - Now).Should().Be(TimeSpan.FromMinutes(1));
        (Pacing.NextSendAt(Now, 100_000, 0.5) - Now).Should().Be(TimeSpan.FromMilliseconds(100));
    }
}
