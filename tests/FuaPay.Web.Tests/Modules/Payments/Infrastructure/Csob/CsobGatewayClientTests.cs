using System.Net;
using System.Text;
using System.Text.Json;

using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

namespace FuaPay.Web.Tests.Modules.Payments.Infrastructure.Csob;

public sealed class CsobGatewayClientTests
{
    private static readonly DateTimeOffset CurrentTime =
        new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EchoAsync_VerifiesSignedResponse()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = "OK",
                signature = "gateway-signature"
            });
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            });
        var signature = new RecordingSignature
        {
            VerificationResult = true
        };
        var client = CreateClient(handler, signature);

        var result = await client.EchoAsync();

        Assert.Equal(0, result.ResultCode);
        Assert.Equal("OK", result.ResultMessage);
        Assert.Equal(
            "M1MIPS0000|20260701120000",
            Assert.Single(signature.SignedTexts));
        Assert.Equal(
            "20260701120001|0|OK",
            Assert.Single(signature.VerifiedTexts));
    }

    [Fact]
    public async Task EchoAsync_VerifiesExactUnmodifiedResponseValues()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = " OK ",
                signature = "gateway-signature"
            });
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            });
        var signature = new RecordingSignature
        {
            VerificationResult = true
        };
        var client = CreateClient(handler, signature);

        var result = await client.EchoAsync();

        Assert.Equal(" OK ", result.ResultMessage);
        Assert.Equal(
            "20260701120001|0| OK ",
            Assert.Single(signature.VerifiedTexts));
    }

    [Fact]
    public async Task InitializeAsync_SignsRequestAndReturnsBrowserProcessUri()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 1,
                signature = "gateway-signature"
            });
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            });
        var signature = new RecordingSignature
        {
            VerificationResult = true
        };
        var client = CreateClient(handler, signature);
        var merchantData = Convert.ToBase64String([1, 2, 3]);

        var result = await client.InitializeAsync(
            new CsobPaymentInit(
                " 5547 ",
                123400,
                [new CsobPaymentCartItem("  Kredit  ", 1, 123400, "   ")],
                $" {merchantData} "));

        Assert.Equal("ff41e84b7e33@HA", result.PayId);
        Assert.Equal(1, result.PaymentStatus);
        Assert.Equal(0, result.ResultCode);
        Assert.Equal("OK", result.ResultMessage);
        Assert.StartsWith(
            "https://iapi.iplatebnibrana.csob.cz/api/v1.9/payment/process/",
            result.ProcessUri.AbsoluteUri,
            StringComparison.Ordinal);
        Assert.Equal(
            "/api/v1.9/payment/init",
            handler.Request!.RequestUri!.AbsolutePath);
        Assert.Contains(
            "\"orderNo\":\"5547\"",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            $"\"merchantData\":\"{merchantData}\"",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"name\":\"Kredit\"",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "description",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "M1MIPS0000|5547|20260701120000|payment|card|123400|CZK|true|",
            signature.SignedTexts[0],
            StringComparison.Ordinal);
        Assert.Equal(
            "ff41e84b7e33@HA|20260701120001|0|OK|1",
            Assert.Single(signature.VerifiedTexts));
    }

    [Fact]
    public async Task GetStatusAsync_RejectsInvalidGatewaySignature()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 7,
                authCode = "F7A23E",
                statusDetail = "detail",
                signature = "gateway-signature"
            });
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            });
        var signature = new RecordingSignature
        {
            VerificationResult = false
        };
        var client = CreateClient(handler, signature);

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.GetStatusAsync("ff41e84b7e33@HA"));

        Assert.Contains(
            "podpis",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "ff41e84b7e33@HA|20260701120001|0|OK|7|F7A23E|detail",
            Assert.Single(signature.VerifiedTexts));
    }

    [Fact]
    public async Task GetStatusAsync_NonSuccessHttpResponseIsNeverTreatedAsSigned()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    "{\"resultCode\":110,\"resultMessage\":\"Wrong signature\"}",
                    Encoding.UTF8,
                    "application/json")
            });
        var signature = new RecordingSignature
        {
            VerificationResult = true
        };
        var client = CreateClient(handler, signature);

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.GetStatusAsync("ff41e84b7e33@HA"));

        Assert.Null(exception.ResultCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            exception.HttpStatusCode);
        Assert.Contains(
            "Neověřený diagnostický kód brány: 110",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(signature.VerifiedTexts);
    }


    [Fact]
    public async Task GetStatusAsync_RejectsSignedResponseForDifferentPayId()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "aa41e84b7e33",
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 7,
                signature = "gateway-signature"
            });
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            });
        var signature = new RecordingSignature
        {
            VerificationResult = true
        };
        var client = CreateClient(handler, signature);

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.GetStatusAsync("ff41e84b7e33"));

        Assert.Contains(
            "jiné platbě",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(signature.VerifiedTexts);
    }

    [Fact]
    public async Task InitializeAsync_RejectsStaleSignedResponse()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm = "20260701114959",
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 1,
                signature = "gateway-signature"
            });
        var signature = new RecordingSignature
        {
            VerificationResult = true
        };
        var client = CreateClient(
            new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            }),
            signature);

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.InitializeAsync(CreatePaymentInit()));

        Assert.Contains(
            "časové okno",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(signature.VerifiedTexts);
    }

    [Fact]
    public async Task GetStatusAsync_RejectsFutureSignedResponse()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm = "20260701121001",
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 1,
                signature = "gateway-signature"
            });
        var signature = new RecordingSignature
        {
            VerificationResult = true
        };
        var client = CreateClient(
            new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            }),
            signature);

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.GetStatusAsync("ff41e84b7e33@HA"));

        Assert.Contains(
            "časové okno",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(signature.VerifiedTexts);
    }

    [Fact]
    public async Task EchoAsync_RejectsNonexistentSpringDstLocalTime()
    {
        var client = CreateEchoClient(
            "20260329023000",
            new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero));

        await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.EchoAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task EchoAsync_RejectsBothOccurrencesOfAmbiguousAutumnDstTime(
        int utcHour)
    {
        var client = CreateEchoClient(
            "20261025023000",
            new DateTimeOffset(2026, 10, 25, utcHour, 30, 0, TimeSpan.Zero));

        await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.EchoAsync());
    }

    [Fact]
    public async Task EchoAsync_RejectsOneHourReplayAcrossAutumnDstOverlap()
    {
        var client = CreateEchoClient(
            "20261025021500",
            new DateTimeOffset(2026, 10, 25, 1, 15, 0, TimeSpan.Zero));

        await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.EchoAsync());
    }

    [Theory]
    [InlineData("20260701115500", true)]
    [InlineData("20260701115459", false)]
    [InlineData("20260701120500", true)]
    [InlineData("20260701120501", false)]
    public async Task EchoAsync_EnforcesExactClockSkewBoundaries(
        string dttm,
        bool accepted)
    {
        var client = CreateEchoClient(dttm, CurrentTime);

        if (accepted)
        {
            await client.EchoAsync();
        }
        else
        {
            await Assert.ThrowsAsync<CsobGatewayException>(
                () => client.EchoAsync());
        }
    }

    [Fact]
    public async Task InitializeAsync_RejectsWhitespaceRewrittenSignedPayId()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = " ff41e84b7e33 ",
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 1,
                signature = "gateway-signature"
            });
        var client = CreateClient(
            new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            }),
            new RecordingSignature { VerificationResult = true });

        await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.InitializeAsync(CreatePaymentInit()));
    }

    [Fact]
    public async Task GetStatusAsync_RejectsPayIdLongerThanProtocolLimit()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(
            handler,
            new RecordingSignature());

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.GetStatusAsync("1234567890123456"));

        Assert.Contains(
            "15",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task EchoAsync_TransportFailureBecomesTypedGatewayFailure()
    {
        var handler = new ThrowingHandler(
            new HttpRequestException("network unavailable"));
        var client = CreateClient(
            handler,
            new RecordingSignature());

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.EchoAsync());

        Assert.Contains(
            "není dostupná",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task EchoAsync_TimeoutBecomesTypedGatewayFailure()
    {
        var handler = new ThrowingHandler(new TaskCanceledException());
        var client = CreateClient(
            handler,
            new RecordingSignature());

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.EchoAsync());

        Assert.Contains(
            "překročilo",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.IsType<TaskCanceledException>(exception.InnerException);
    }


    [Fact]
    public async Task EchoAsync_CallerCancellationIsNotReclassifiedAsTimeout()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new ThrowingHandler(new TaskCanceledException());
        var client = CreateClient(
            handler,
            new RecordingSignature());

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => client.EchoAsync(cancellation.Token));
    }

    [Fact]
    public async Task EchoAsync_InvalidSignedResponseStructureIsRejected()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                dttm = "invalid",
                resultCode = 0,
                resultMessage = "OK",
                signature = "gateway-signature"
            });
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            });
        var signature = new RecordingSignature
        {
            VerificationResult = true
        };
        var client = CreateClient(handler, signature);

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.EchoAsync());

        Assert.Contains(
            "strukturu",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Empty(signature.VerifiedTexts);
    }

    private static CsobGatewayClient CreateClient(
        HttpMessageHandler handler,
        RecordingSignature signature,
        DateTimeOffset? currentTime = null)
    {
        var configuration = new CsobGatewayConfiguration(
            Enabled: true,
            CsobGatewayConfiguration.SandboxApiBaseUri,
            "M1MIPS0000",
            "unused-private-key",
            "unused-public-key",
            new Uri("https://shop.example.com/payments/csob/return"),
            900,
            TimeSpan.FromSeconds(30));
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = configuration.ApiBaseUri
        };

        return new CsobGatewayClient(
            httpClient,
            configuration,
            new CsobGatewayAvailability(true),
            signature,
            new FixedTimeProvider(currentTime ?? CurrentTime));
    }

    private static CsobGatewayClient CreateEchoClient(
        string dttm,
        DateTimeOffset currentTime)
    {
        var response = JsonSerializer.Serialize(
            new
            {
                dttm,
                resultCode = 0,
                resultMessage = "OK",
                signature = "gateway-signature"
            });

        return CreateClient(
            new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            }),
            new RecordingSignature { VerificationResult = true },
            currentTime);
    }

    private static CsobPaymentInit CreatePaymentInit() =>
        new(
            "5547",
            123400,
            [new CsobPaymentCartItem("Kredit", 1, 123400)],
            Convert.ToBase64String([1, 2, 3]));

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _currentTime;

        public FixedTimeProvider(DateTimeOffset currentTime)
        {
            _currentTime = currentTime;
        }

        public override DateTimeOffset GetUtcNow() => _currentTime;
    }

    private sealed class RecordingSignature : ICsobGatewaySignature
    {
        public List<string> SignedTexts { get; } = [];

        public List<string> VerifiedTexts { get; } = [];

        public bool VerificationResult { get; init; }

        public string Sign(string textToSign)
        {
            SignedTexts.Add(textToSign);
            return "merchant-signature";
        }

        public bool Verify(string textToSign, string signature)
        {
            VerifiedTexts.Add(textToSign);
            return VerificationResult;
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(_exception);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public RecordingHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public HttpRequestMessage? Request { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _response;
        }
    }
}
