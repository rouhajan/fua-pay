using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Tests.Modules.Payments.Domain;

public sealed class PaymentInitiationTests
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 8, 13, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CorrelationData_RoundTripsExpectedBinding()
    {
        var paymentId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var encoded = PaymentProviderCorrelation.Encode(
            paymentId,
            correlationId);

        var payload = Convert.FromBase64String(encoded);
        Assert.Equal(33, payload.Length);
        Assert.Equal(1, payload[0]);
        Assert.Equal(paymentId, new Guid(payload.AsSpan(1, 16)));
        Assert.Equal(correlationId, new Guid(payload.AsSpan(17, 16)));
    }

    [Fact]
    public void Complete_PersistsValidatedHttpsProcessUri()
    {
        var initiation = new PaymentInitiation(
            Guid.NewGuid(),
            PaymentProvider.Csob,
            123,
            Guid.NewGuid(),
            TestTime);
        var processUri = new Uri("https://example.test/process/123");

        initiation.Begin(TestTime);
        initiation.Complete(TestTime, processUri);

        Assert.Equal(PaymentInitiationState.Initialized, initiation.State);
        Assert.Equal(processUri, initiation.ProcessUri);
        Assert.Null(initiation.LastError);
    }

    [Fact]
    public void MarkUncertain_WithObservedProviderResult_CanRecoverWithoutInitReplay()
    {
        var initiation = new PaymentInitiation(
            Guid.NewGuid(),
            PaymentProvider.Csob,
            123,
            Guid.NewGuid(),
            TestTime);
        var processUri = new Uri("https://example.test/process/recovered");

        initiation.Begin(TestTime);
        initiation.MarkUncertain(
            "local commit failed",
            TestTime.AddSeconds(1),
            "PAY-RECOVERED",
            processUri);

        Assert.Equal(PaymentInitiationState.Uncertain, initiation.State);
        Assert.Equal("PAY-RECOVERED", initiation.ObservedProviderReference);
        Assert.Equal(processUri, initiation.ObservedProcessUri);

        initiation.RecoverObservedInitialization(TestTime.AddSeconds(2));

        Assert.Equal(PaymentInitiationState.Initialized, initiation.State);
        Assert.Equal(processUri, initiation.ProcessUri);
        Assert.Null(initiation.ObservedProviderReference);
        Assert.Null(initiation.ObservedProcessUri);
        Assert.Null(initiation.LastError);
    }

    [Fact]
    public void MarkUncertain_ObservedProcessUriWithoutReference_IsRejected()
    {
        var initiation = new PaymentInitiation(
            Guid.NewGuid(),
            PaymentProvider.Csob,
            123,
            Guid.NewGuid(),
            TestTime);
        initiation.Begin(TestTime);

        Assert.Throws<ArgumentException>(
            () => initiation.MarkUncertain(
                "ambiguous",
                TestTime,
                observedProviderReference: null,
                observedProcessUri: new Uri(
                    "https://example.test/process/123")));

        Assert.Equal(PaymentInitiationState.InProgress, initiation.State);
        Assert.Null(initiation.LastError);
        Assert.Null(initiation.ObservedProviderReference);
        Assert.Null(initiation.ObservedProcessUri);
    }

    [Fact]
    public void Complete_RejectsNonHttpsProcessUri()
    {
        var initiation = new PaymentInitiation(
            Guid.NewGuid(),
            PaymentProvider.Csob,
            123,
            Guid.NewGuid(),
            TestTime);
        initiation.Begin(TestTime);

        Assert.Throws<ArgumentException>(
            () => initiation.Complete(
                TestTime,
                new Uri("http://example.test/process/123")));
    }
}
