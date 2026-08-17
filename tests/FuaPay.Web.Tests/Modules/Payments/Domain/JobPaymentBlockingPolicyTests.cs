using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Tests.Modules.Payments.Domain;

public sealed class JobPaymentBlockingPolicyTests
{
    [Theory]
    [InlineData(PaymentStatus.Created, true)]
    [InlineData(PaymentStatus.Pending, true)]
    [InlineData(PaymentStatus.Succeeded, true)]
    [InlineData(PaymentStatus.Failed, false)]
    [InlineData(PaymentStatus.Cancelled, false)]
    [InlineData(PaymentStatus.Expired, false)]
    public void IsBlocking_DefinesOneSemanticRule(
        PaymentStatus status,
        bool expected)
    {
        Assert.Equal(expected, JobPaymentBlockingPolicy.IsBlocking(status));
    }
}
