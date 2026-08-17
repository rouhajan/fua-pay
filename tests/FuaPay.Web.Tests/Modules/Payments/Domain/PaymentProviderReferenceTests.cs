using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Tests.Modules.Payments.Domain;

public sealed class PaymentProviderReferenceTests
{
    [Fact]
    public void Normalize_TrimsOrdinaryOuterWhitespace()
    {
        var result = PaymentProviderReference.Normalize(
            "  PROVIDER-123  ");

        Assert.Equal("PROVIDER-123", result);
    }

    [Theory]
    [InlineData("REF\u0000")]
    [InlineData("REF\u200E")]
    [InlineData("REF\u2028")]
    [InlineData("REF\u2029")]
    public void Normalize_RejectsUnsafeUnicode(string value)
    {
        Assert.Throws<ArgumentException>(
            () => PaymentProviderReference.Normalize(value));
    }

    [Fact]
    public void Normalize_RejectsBlankValue()
    {
        Assert.Throws<ArgumentException>(
            () => PaymentProviderReference.Normalize("   "));
    }

    [Fact]
    public void Normalize_RejectsValueOverMaximumLength()
    {
        var value = new string(
            'A',
            PaymentProviderReference.MaxLength + 1);

        Assert.Throws<ArgumentException>(
            () => PaymentProviderReference.Normalize(value));
    }

    [Fact]
    public void Normalize_RejectsInvalidUtf16()
    {
        var value = "REF-" + '\uD800';

        Assert.Throws<ArgumentException>(
            () => PaymentProviderReference.Normalize(value));
    }

    [Fact]
    public void Normalize_AcceptsValidSupplementaryRune()
    {
        var value = "REF-\U0001F680";

        Assert.Equal(
            value,
            PaymentProviderReference.Normalize(value));
    }
}
