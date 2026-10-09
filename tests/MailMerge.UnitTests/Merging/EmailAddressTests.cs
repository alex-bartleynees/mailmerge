using MailMerge.Api.Shared.Merging;

namespace MailMerge.UnitTests.Merging;

public sealed class EmailAddressTests
{
    [Theory]
    [InlineData("ann@example.com")]
    [InlineData(" BOB@Example.co.nz ")]
    [InlineData("first.last+tag@sub.domain.org")]
    public void Accepts_ordinary_addresses(string email) => EmailAddress.IsValid(email).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("a@b.c")]
    [InlineData("a b@example.com")]
    [InlineData("ann@example.com, bob@example.com")]
    [InlineData("<ann@example.com>")]
    public void Rejects_junk(string email) => EmailAddress.IsValid(email).Should().BeFalse();

    [Fact]
    public void Normalize_trims_and_lower_cases() =>
        EmailAddress.Normalize("  BOB@Example.com ").Should().Be("bob@example.com");
}
