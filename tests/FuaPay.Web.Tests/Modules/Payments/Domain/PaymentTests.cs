using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Tests.Modules.Payments.Domain;

public sealed class PaymentTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 29, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_CreatesNewPayment()
    {
        var payment = CreatePayment();

        Assert.Equal(PaymentStatus.Created, payment.Status);
        Assert.Null(payment.ProviderReference);
        Assert.Null(payment.CompletedAt);
    }

    [Fact]
    public void MarkPending_NormalizesReference()
    {
        var payment = CreatePayment();

        payment.MarkPending("  DEV-123  ", CreatedAt);

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal("DEV-123", payment.ProviderReference);
    }

    [Fact]
    public void MarkPending_RejectsUnsafeProviderReference()
    {
        var payment = CreatePayment();

        Assert.Throws<ArgumentException>(
            () => payment.MarkPending(
                "DEV-123\u2028",
                CreatedAt));
    }

    [Fact]
    public void Complete_IsIdempotentAfterSuccess()
    {
        var payment = CreatePendingPayment();
        var completedAt = CreatedAt.AddMinutes(1);

        Assert.True(payment.Complete(completedAt));
        Assert.False(payment.Complete(completedAt.AddMinutes(1)));
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(completedAt, payment.CompletedAt);
    }

    [Fact]
    public void Fail_RequiresNonEmptyReason()
    {
        var payment = CreatePendingPayment();

        Assert.Throws<ArgumentException>(
            () => payment.Fail(" ", CreatedAt.AddMinutes(1)));
    }

    [Fact]
    public void Cancel_AfterSuccess_IsRejected()
    {
        var payment = CreatePendingPayment();
        payment.Complete(CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidPaymentStateTransitionException>(
            () => payment.Cancel(CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Constructor_RejectsInconsistentJobPurpose()
    {
        Assert.Throws<ArgumentException>(
            () => new Payment(
                Guid.NewGuid(),
                Guid.NewGuid(),
                PaymentPurposeType.Job,
                jobId: null,
                new Money(10_000),
                PaymentProvider.Development,
                CreatedAt));
    }

    private static Payment CreatePayment()
    {
        return new Payment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(10_000),
            PaymentProvider.Development,
            CreatedAt,
            Guid.NewGuid());
    }

    private static Payment CreatePendingPayment()
    {
        var payment = CreatePayment();
        payment.MarkPending("DEV-123", CreatedAt);
        return payment;
    }
}
