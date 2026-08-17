using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Pages;

namespace FuaPay.Web.Tests.Pages;

public sealed class PaymentDisplayTests
{
    [Theory]
    [InlineData(PaymentStatus.Failed, true)]
    [InlineData(PaymentStatus.Cancelled, true)]
    [InlineData(PaymentStatus.Expired, true)]
    [InlineData(PaymentStatus.Created, false)]
    [InlineData(PaymentStatus.Pending, false)]
    [InlineData(PaymentStatus.Succeeded, false)]
    public void IsUnsuccessfulTerminalStatus_RecognizesRetryableOutcomes(
        PaymentStatus status,
        bool expected)
    {
        Assert.Equal(
            expected,
            PaymentDisplay.IsUnsuccessfulTerminalStatus(status));
    }

    [Theory]
    [InlineData(PaymentStatus.Created, "is-awaiting")]
    [InlineData(PaymentStatus.Pending, "is-awaiting")]
    [InlineData(PaymentStatus.Succeeded, "is-ready")]
    [InlineData(PaymentStatus.Failed, "is-failed")]
    [InlineData(PaymentStatus.Cancelled, "is-cancelled")]
    [InlineData(PaymentStatus.Expired, "is-cancelled")]
    public void StatusCssClass_UsesSharedBadgeClasses(
        PaymentStatus status,
        string expected)
    {
        Assert.Equal(expected, PaymentDisplay.StatusCssClass(status));
    }
}
