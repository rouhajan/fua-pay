using System.Text;

using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.Web.Tests.Modules.Payments.Infrastructure.Csob;

public sealed class CsobPaymentReturnEndpointTests
{
    private static readonly DateTimeOffset ReceivedAt =
        new(2026, 1, 15, 11, 0, 30, TimeSpan.Zero);
    private const string Dttm = "20260115120000";
    private const string PayId = "pay1234567890";
    private const string ResultMessage = "Session expired";
    private const string MerchantData = "AQIDBA==";
    private const string Signature = "valid-signature";
    private const string ExpiryTextToSign =
        "pay1234567890|20260115120000|130|Session expired|6|AQIDBA==";

    [Fact]
    public async Task HandleAsync_Get_VerifiesSignedExpiryAndSchedulesReconciliation()
    {
        var paymentId = Guid.NewGuid();
        var scheduler = new RecordingRecoveryScheduler(paymentId);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.PathBase = "/fuapay";
        context.Request.QueryString = CreateExpiryQueryString();

        await CsobPaymentReturnEndpoint.HandleAsync(
            context,
            scheduler,
            CreateVerifier(),
            CancellationToken.None);

        Assert.Equal(PayId, scheduler.VerifiedReturn?.PayId);
        Assert.True(scheduler.VerifiedReturn?.IsExpired);
        Assert.Equal(ExpiryTextToSign, scheduler.VerifiedReturn?.TextToSign);
        Assert.Equal(StatusCodes.Status303SeeOther, context.Response.StatusCode);
        Assert.Equal(
            $"/fuapay/Customer/Payments/Details/{paymentId:D}" +
            "?view=customer&waitForReconciliation=true",
            context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task HandleAsync_Post_VerifiesSignedExpiryAndSchedulesReconciliation()
    {
        var paymentId = Guid.NewGuid();
        var scheduler = new RecordingRecoveryScheduler(paymentId);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes(
                CreateExpiryQueryString().Value![1..]));

        await CsobPaymentReturnEndpoint.HandleAsync(
            context,
            scheduler,
            CreateVerifier(),
            CancellationToken.None);

        Assert.Equal(PayId, scheduler.VerifiedReturn?.PayId);
        Assert.True(scheduler.VerifiedReturn?.IsExpired);
        Assert.Equal(StatusCodes.Status303SeeOther, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_Get_BuildsSignatureTextInDocumentedOptionalFieldOrder()
    {
        const string expectedTextToSign =
            "pay1234567890|20260115120000|0|OK|7|AUTH01|AQIDBA==|detail";
        var scheduler = new RecordingRecoveryScheduler(Guid.NewGuid());
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.QueryString = QueryString.Create(
            new Dictionary<string, string?>
            {
                ["statusDetail"] = "detail",
                ["merchantData"] = MerchantData,
                ["paymentStatus"] = "7",
                ["authCode"] = "AUTH01",
                ["resultMessage"] = "OK",
                ["resultCode"] = "0",
                ["dttm"] = Dttm,
                ["payId"] = PayId,
                ["signature"] = Signature
            });

        await CsobPaymentReturnEndpoint.HandleAsync(
            context,
            scheduler,
            CreateVerifier(expectedTextToSign: expectedTextToSign),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status303SeeOther, context.Response.StatusCode);
        Assert.Equal(expectedTextToSign, scheduler.VerifiedReturn?.TextToSign);
    }

    [Theory]
    [InlineData("signature", "tampered-signature")]
    [InlineData("resultCode", "0")]
    [InlineData("paymentStatus", "3")]
    public async Task HandleAsync_TamperedSignedReturnFailsClosed(
        string parameter,
        string tamperedValue)
    {
        var scheduler = new RecordingRecoveryScheduler(Guid.NewGuid());
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.QueryString = CreateExpiryQueryString(
            parameter,
            tamperedValue);

        await CsobPaymentReturnEndpoint.HandleAsync(
            context,
            scheduler,
            CreateVerifier(),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Null(scheduler.VerifiedReturn);
    }

    [Fact]
    public async Task HandleAsync_StaleSignedReturnFailsClosed()
    {
        var scheduler = new RecordingRecoveryScheduler(Guid.NewGuid());
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.QueryString = CreateExpiryQueryString();

        await CsobPaymentReturnEndpoint.HandleAsync(
            context,
            scheduler,
            CreateVerifier(ReceivedAt.AddMinutes(6)),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Null(scheduler.VerifiedReturn);
    }

    [Theory]
    [InlineData("&payId=pay1234567890")]
    [InlineData("&unexpected=value")]
    public async Task HandleAsync_DuplicateOrUnknownParameterFailsClosed(
        string suffix)
    {
        var scheduler = new RecordingRecoveryScheduler(Guid.NewGuid());
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.QueryString = new QueryString(
            CreateExpiryQueryString().Value + suffix);

        await CsobPaymentReturnEndpoint.HandleAsync(
            context,
            scheduler,
            CreateVerifier(),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Null(scheduler.VerifiedReturn);
    }

    [Fact]
    public async Task HandleAsync_MissingPayId_ReturnsBadRequest()
    {
        var scheduler = new RecordingRecoveryScheduler(Guid.NewGuid());
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;

        await CsobPaymentReturnEndpoint.HandleAsync(
            context,
            scheduler,
            CreateVerifier(),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Null(scheduler.VerifiedReturn);
    }

    [Fact]
    public async Task HandleAsync_PostWithoutFormContentType_IsRejected()
    {
        var scheduler = new RecordingRecoveryScheduler(Guid.NewGuid());
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/json";

        await CsobPaymentReturnEndpoint.HandleAsync(
            context,
            scheduler,
            CreateVerifier(),
            CancellationToken.None);

        Assert.Equal(
            StatusCodes.Status415UnsupportedMediaType,
            context.Response.StatusCode);
        Assert.Null(scheduler.VerifiedReturn);
    }

    [Fact]
    public async Task HandleAsync_OversizedPostReturns413WithoutScheduling()
    {
        var scheduler = new RecordingRecoveryScheduler(Guid.NewGuid());
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(
            new byte[CsobPaymentReturnEndpoint.MaximumRequestBodyBytes + 1]);

        await CsobPaymentReturnEndpoint.HandleAsync(
            context,
            scheduler,
            CreateVerifier(),
            CancellationToken.None);

        Assert.Equal(
            StatusCodes.Status413PayloadTooLarge,
            context.Response.StatusCode);
        Assert.Null(scheduler.VerifiedReturn);
    }

    [Fact]
    public async Task EndpointRateLimitRejectsRequestBeforeHandlerSideEffects()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ICsobPaymentRecoveryScheduler>(
            new RecordingRecoveryScheduler(Guid.NewGuid()));
        builder.Services.AddSingleton<ICsobGatewaySignature>(
            new ExpectedSignature(ExpiryTextToSign));
        builder.Services.AddSingleton(
            new CsobPaymentReturnVerifier(
                new ExpectedSignature(ExpiryTextToSign),
                new FixedTimeProvider(ReceivedAt)));
        builder.Services.AddCsobPaymentGateway(
            new CsobGatewayConfiguration(
                Enabled: false,
                CsobGatewayConfiguration.SandboxApiBaseUri,
                string.Empty,
                string.Empty,
                string.Empty,
                new Uri("https://localhost/payments/csob/return"),
                900,
                TimeSpan.FromSeconds(30)));

        await using var app = builder.Build();
        app.UseRouting();
        app.UseRateLimiter();
        app.MapCsobPaymentReturn();
        await app.StartAsync();
        var client = app.GetTestClient();

        for (var index = 0; index < 30; index++)
        {
            using var accepted = await client.GetAsync(
                "/payments/csob/return");
            Assert.Equal(
                StatusCodes.Status400BadRequest,
                (int)accepted.StatusCode);
        }

        using var rejected = await client.GetAsync(
            "/payments/csob/return?payId=pay1234567890");

        Assert.Equal(
            StatusCodes.Status429TooManyRequests,
            (int)rejected.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_UnknownLocalPayId_ReturnsBadRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.QueryString = CreateExpiryQueryString(
            "payId",
            "unknown-pay-id");

        await CsobPaymentReturnEndpoint.HandleAsync(
            context,
            new ThrowingRecoveryScheduler(),
            CreateVerifier(
                expectedTextToSign:
                    "unknown-pay-id|20260115120000|130|Session expired|6|AQIDBA=="),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    private sealed class RecordingRecoveryScheduler :
        ICsobPaymentRecoveryScheduler
    {
        private readonly Guid _paymentId;

        public RecordingRecoveryScheduler(Guid paymentId)
        {
            _paymentId = paymentId;
        }

        public CsobVerifiedPaymentReturn? VerifiedReturn { get; private set; }

        public Task<Guid> ScheduleReturnAsync(
            CsobVerifiedPaymentReturn verifiedReturn,
            CancellationToken cancellationToken = default)
        {
            VerifiedReturn = verifiedReturn;
            return Task.FromResult(_paymentId);
        }
    }

    private sealed class ThrowingRecoveryScheduler :
        ICsobPaymentRecoveryScheduler
    {
        public Task<Guid> ScheduleReturnAsync(
            CsobVerifiedPaymentReturn verifiedReturn,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<Guid>(
                new PaymentProviderReferenceNotFoundException(
                    PaymentProvider.Csob,
                    verifiedReturn.PayId));
        }
    }

    private static QueryString CreateExpiryQueryString(
        string? replacementName = null,
        string? replacementValue = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["payId"] = PayId,
            ["dttm"] = Dttm,
            ["resultCode"] = "130",
            ["resultMessage"] = ResultMessage,
            ["paymentStatus"] = "6",
            ["merchantData"] = MerchantData,
            ["signature"] = Signature
        };

        if (replacementName is not null)
        {
            values[replacementName] = replacementValue;
        }

        return QueryString.Create(values);
    }

    private static CsobPaymentReturnVerifier CreateVerifier(
        DateTimeOffset? receivedAt = null,
        string expectedTextToSign = ExpiryTextToSign) =>
        new(
            new ExpectedSignature(expectedTextToSign),
            new FixedTimeProvider(receivedAt ?? ReceivedAt));

    private sealed class ExpectedSignature : ICsobGatewaySignature
    {
        private readonly string _expectedTextToSign;

        public ExpectedSignature(string expectedTextToSign)
        {
            _expectedTextToSign = expectedTextToSign;
        }

        public string Sign(string textToSign) => throw new NotSupportedException();

        public bool Verify(string textToSign, string signature) =>
            string.Equals(
                _expectedTextToSign,
                textToSign,
                StringComparison.Ordinal) &&
            string.Equals(Signature, signature, StringComparison.Ordinal);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
