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
    [Fact]
    public async Task HandleAsync_Get_UsesOnlyPayIdAndSchedulesVerification()
    {
        var paymentId = Guid.NewGuid();
        var scheduler = new RecordingRecoveryScheduler(paymentId);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.PathBase = "/fuapay";
        context.Request.QueryString = new QueryString(
            "?payId=pay1234567890&paymentStatus=8&resultCode=0" +
            "&merchantData=untrusted&signature=ignored");

        await CsobPaymentReturnEndpoint.HandleAsync(
            context,
            scheduler,
            CancellationToken.None);

        Assert.Equal("pay1234567890", scheduler.PayId);
        Assert.Equal(StatusCodes.Status303SeeOther, context.Response.StatusCode);
        Assert.Equal(
            $"/fuapay/Customer/Payments/Details?id={paymentId:D}&view=customer",
            context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task HandleAsync_Post_AcceptsFormEncodedGatewayReturn()
    {
        var paymentId = Guid.NewGuid();
        var scheduler = new RecordingRecoveryScheduler(paymentId);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(
            Encoding.UTF8.GetBytes(
                "payId=pay1234567890&paymentStatus=3&signature=ignored"));

        await CsobPaymentReturnEndpoint.HandleAsync(
            context,
            scheduler,
            CancellationToken.None);

        Assert.Equal("pay1234567890", scheduler.PayId);
        Assert.Equal(StatusCodes.Status303SeeOther, context.Response.StatusCode);
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
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Null(scheduler.PayId);
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
            CancellationToken.None);

        Assert.Equal(
            StatusCodes.Status415UnsupportedMediaType,
            context.Response.StatusCode);
        Assert.Null(scheduler.PayId);
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
            CancellationToken.None);

        Assert.Equal(
            StatusCodes.Status413PayloadTooLarge,
            context.Response.StatusCode);
        Assert.Null(scheduler.PayId);
    }

    [Fact]
    public async Task EndpointRateLimitRejectsRequestBeforeHandlerSideEffects()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ICsobPaymentRecoveryScheduler>(
            new RecordingRecoveryScheduler(Guid.NewGuid()));
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
        context.Request.QueryString = new QueryString(
            "?payId=unknown-pay-id");

        await CsobPaymentReturnEndpoint.HandleAsync(
            context,
            new ThrowingRecoveryScheduler(),
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

        public string? PayId { get; private set; }

        public Task<Guid> ScheduleReturnAsync(
            string providerReference,
            CancellationToken cancellationToken = default)
        {
            PayId = providerReference;
            return Task.FromResult(_paymentId);
        }
    }

    private sealed class ThrowingRecoveryScheduler :
        ICsobPaymentRecoveryScheduler
    {
        public Task<Guid> ScheduleReturnAsync(
            string providerReference,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<Guid>(
                new PaymentProviderReferenceNotFoundException(
                    PaymentProvider.Csob,
                    providerReference));
        }
    }
}
