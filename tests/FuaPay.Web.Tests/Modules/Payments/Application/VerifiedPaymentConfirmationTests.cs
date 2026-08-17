using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Tests.Modules.Payments.Application;

public sealed class VerifiedPaymentConfirmationTests
{
    [Fact]
    public void Constructor_NormalizesProviderReference()
    {
        var confirmation = new VerifiedPaymentConfirmation(
            PaymentProvider.Csob,
            "  PAY-123  ",
            new Money(12_500));

        Assert.Equal(
            PaymentProvider.Csob,
            confirmation.Provider);
        Assert.Equal("PAY-123", confirmation.ProviderReference);
        Assert.Equal(new Money(12_500), confirmation.Amount);
    }

    [Fact]
    public void Constructor_RejectsUnknownProvider()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VerifiedPaymentConfirmation(
                PaymentProvider.Unknown,
                "PAY-123",
                new Money(12_500)));
    }

    [Fact]
    public void Constructor_RejectsNonPositiveAmount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VerifiedPaymentConfirmation(
                PaymentProvider.Csob,
                "PAY-123",
                Money.Zero));
    }
}
