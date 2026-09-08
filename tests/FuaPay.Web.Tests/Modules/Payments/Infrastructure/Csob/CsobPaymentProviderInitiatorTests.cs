using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

namespace FuaPay.Web.Tests.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentProviderInitiatorTests
{
    [Theory]
    [InlineData(PaymentPurposeType.CreditTopUp, "Dobití kreditu")]
    [InlineData(PaymentPurposeType.Job, "Úhrada zakázky")]
    public async Task InitializeThenVerifyAsync_MapsProviderNeutralRequestToCsob(
        PaymentPurposeType purposeType,
        string expectedItemName)
    {
        var client = new RecordingClient();
        var initiator = CreateInitiator(client);
        var request = new PaymentProviderInitializationRequest(
            Guid.NewGuid(),
            PaymentProvider.Csob,
            123456,
            Guid.NewGuid(),
            purposeType,
            purposeType == PaymentPurposeType.Job ? Guid.NewGuid() : null,
            new Money(123400));

        var result = await initiator.InitializeAsync(request);

        Assert.Equal(PaymentProvider.Csob, result.Provider);
        Assert.Equal("ff41e84b7e33@HA", result.ProviderReference);
        Assert.Equal(client.Result.ProcessUri, result.ProcessUri);
        Assert.Null(client.StatusPayId);

        await initiator.VerifyAsync(result);

        var payment = Assert.IsType<CsobPaymentInit>(client.Payment);
        Assert.Equal("123456", payment.OrderNumber);
        Assert.Equal(123400, payment.TotalAmountMinorUnits);
        Assert.Equal(request.CorrelationData, payment.MerchantData);
        Assert.Equal(result.ProviderReference, client.StatusPayId);
        var item = Assert.Single(payment.Cart);
        Assert.Equal(expectedItemName, item.Name);
        Assert.Equal(1, item.Quantity);
        Assert.Equal(123400, item.AmountMinorUnits);
    }

    [Fact]
    public async Task InitializeAsync_RejectsDifferentProvider()
    {
        var initiator = CreateInitiator(new RecordingClient());
        var request = new PaymentProviderInitializationRequest(
            Guid.NewGuid(),
            PaymentProvider.Development,
            123456,
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(1000));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => initiator.InitializeAsync(request));
    }

    [Fact]
    public async Task VerifyAsync_StatusProbeFailureLeavesCandidateAvailable()
    {
        var failure = new CsobGatewayException("status unavailable");
        var client = new RecordingClient(failure);
        var initiator = CreateInitiator(client);
        var request = new PaymentProviderInitializationRequest(
            Guid.NewGuid(),
            PaymentProvider.Csob,
            123456,
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(1000));

        var candidate = await initiator.InitializeAsync(request);

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => initiator.VerifyAsync(candidate));

        Assert.Equal(
            client.Result.PayId,
            candidate.ProviderReference);
        Assert.Equal(client.Result.ProcessUri, candidate.ProcessUri);
        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task InitializeAsync_SignedDeclinePreservesPayIdWithoutSuccess()
    {
        var client = new RecordingClient
        {
            Result = new CsobPaymentInitResult(
                "ff41e84b7e33@HA",
                6,
                new Uri("https://example.test/process/declined"),
                ResultCode: 110,
                ResultMessage: "declined")
        };
        var initiator = CreateInitiator(client);
        var request = new PaymentProviderInitializationRequest(
            Guid.NewGuid(),
            PaymentProvider.Csob,
            123456,
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(1000));

        var exception = await Assert.ThrowsAsync<
            PaymentProviderInitializationUncertainException>(
            () => initiator.InitializeAsync(request));

        Assert.Equal(
            client.Result.PayId,
            exception.ObservedResult.ProviderReference);
    }

    [Fact]
    public async Task VerifyAsync_ImmediateTerminalStatusDoesNotReportSuccess()
    {
        var client = new RecordingClient
        {
            StatusPaymentStatus = 6
        };
        var initiator = CreateInitiator(client);
        var request = new PaymentProviderInitializationRequest(
            Guid.NewGuid(),
            PaymentProvider.Csob,
            123456,
            Guid.NewGuid(),
            PaymentPurposeType.CreditTopUp,
            jobId: null,
            new Money(1000));

        var candidate = await initiator.InitializeAsync(request);

        await Assert.ThrowsAsync<CsobGatewayException>(
            () => initiator.VerifyAsync(candidate));

        Assert.Equal(
            client.Result.PayId,
            candidate.ProviderReference);
    }

    [Fact]
    public void ResolveTrustedProcessUri_ExactCsobUriIsAccepted()
    {
        var initiator = CreateInitiator(new RecordingClient());
        const string processUri =
            "https://iapi.iplatebnibrana.csob.cz/api/v1.9/payment/process/" +
            "M1MIPS0000/ff41e84b7e33%40HA/20260908123045/dGVzdA%3D%3D";

        var resolved = initiator.ResolveTrustedProcessUri(
            PaymentProvider.Csob,
            "ff41e84b7e33@HA",
            processUri);

        Assert.Equal(processUri, resolved?.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://iapi.iplatebnibrana.csob.cz/api/v1.9/payment/process/M1MIPS0000/ff41e84b7e33%40HA/20260908123045/signature")]
    [InlineData("not a URI")]
    [InlineData("https://evil.example/api/v1.9/payment/process/M1MIPS0000/ff41e84b7e33%40HA/20260908123045/signature")]
    [InlineData("https://iapi.iplatebnibrana.csob.cz/api/v1.9/payment/refund/M1MIPS0000/ff41e84b7e33%40HA/20260908123045/signature")]
    [InlineData("https://iapi.iplatebnibrana.csob.cz/api/v1.9/payment/process/M1MIPS0000/differentPayId/20260908123045/signature")]
    [InlineData("https://user@iapi.iplatebnibrana.csob.cz/api/v1.9/payment/process/M1MIPS0000/ff41e84b7e33%40HA/20260908123045/signature")]
    [InlineData("https://iapi.iplatebnibrana.csob.cz/api/v1.9/payment/process/M1MIPS0000/ff41e84b7e33%40HA/20260908123045/signature?next=https://evil.example")]
    [InlineData("https://iapi.iplatebnibrana.csob.cz/api/v1.9/payment/process/M1MIPS0000/ff41e84b7e33%40HA/not-a-dttm/signature")]
    [InlineData("https://iapi.iplatebnibrana.csob.cz/api//v1.9/payment/process/M1MIPS0000/ff41e84b7e33%40HA/20260908123045/signature")]
    [InlineData("https://iapi.iplatebnibrana.csob.cz/api/v1.9/payment/process/M1MIPS0000/ff41e84b7e33%40HA/20260908123045/signature/")]
    public void ResolveTrustedProcessUri_UnsafeUriIsRejected(
        string processUri)
    {
        var initiator = CreateInitiator(new RecordingClient());

        var resolved = initiator.ResolveTrustedProcessUri(
            PaymentProvider.Csob,
            "ff41e84b7e33@HA",
            processUri);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveTrustedProcessUri_DifferentProviderIsRejected()
    {
        var initiator = CreateInitiator(new RecordingClient());
        const string processUri =
            "https://iapi.iplatebnibrana.csob.cz/api/v1.9/payment/process/" +
            "M1MIPS0000/ff41e84b7e33%40HA/20260908123045/signature";

        var resolved = initiator.ResolveTrustedProcessUri(
            PaymentProvider.Development,
            "ff41e84b7e33@HA",
            processUri);

        Assert.Null(resolved);
    }

    private static CsobPaymentProviderInitiator CreateInitiator(
        ICsobGatewayClient client) =>
        new(
            client,
            new CsobGatewayAvailability(true),
            new CsobGatewayConfiguration(
                Enabled: true,
                CsobGatewayConfiguration.IntegrationApiBaseUri,
                MerchantId: "M1MIPS0000",
                PrivateKeyPath: "unused-private-key",
                GatewayPublicKeyPath: "unused-public-key",
                ReturnUri: new Uri("https://localhost/csob/return"),
                PaymentTtlSeconds: 900,
                RequestTimeout: TimeSpan.FromSeconds(30)));

    private sealed class RecordingClient : ICsobGatewayClient
    {
        private readonly Exception? _statusException;

        public RecordingClient(Exception? statusException = null)
        {
            _statusException = statusException;
        }

        public CsobPaymentInit? Payment { get; private set; }

        public string? StatusPayId { get; private set; }

        public CsobPaymentInitResult Result { get; set; } = new(
            "ff41e84b7e33@HA",
            1,
            new Uri(
                "https://iapi.iplatebnibrana.csob.cz/api/v1.9/payment/process/test"));

        public int StatusPaymentStatus { get; set; } = 1;

        public Task<CsobEchoResult> EchoAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobEchoResult> EchoPostAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentInitResult> InitializeAsync(
            CsobPaymentInit payment,
            CancellationToken cancellationToken = default)
        {
            Payment = payment;
            return Task.FromResult(Result);
        }

        public Task<CsobPaymentStatusResult> GetStatusAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            StatusPayId = payId;

            if (_statusException is not null)
            {
                return Task.FromException<CsobPaymentStatusResult>(
                    _statusException);
            }

            return Task.FromResult(new CsobPaymentStatusResult(
                payId,
                0,
                "OK",
                StatusPaymentStatus,
                AuthCode: null,
                StatusDetail: null));
        }
    }
}
