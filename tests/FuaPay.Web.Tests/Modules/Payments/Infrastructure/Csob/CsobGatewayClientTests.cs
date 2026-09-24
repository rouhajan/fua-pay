using System.Globalization;
using System.Net;
using System.Security.Cryptography;
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
    public async Task EchoPostAsync_UsesOfficialJsonContractAndVerifiesResponse()
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

        var result = await client.EchoPostAsync();

        Assert.Equal(0, result.ResultCode);
        Assert.Equal("OK", result.ResultMessage);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(
            "/api/v1.9/echo",
            handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal(
            "application/json",
            handler.Request.Content!.Headers.ContentType!.MediaType);
        using var request = JsonDocument.Parse(handler.RequestBody);
        var root = request.RootElement;
        Assert.Equal(3, root.EnumerateObject().Count());
        Assert.Equal("M1MIPS0000", root.GetProperty("merchantId").GetString());
        Assert.Equal("20260701120000", root.GetProperty("dttm").GetString());
        Assert.Equal(
            "merchant-signature",
            root.GetProperty("signature").GetString());
        Assert.Equal(
            "M1MIPS0000|20260701120000",
            Assert.Single(signature.SignedTexts));
        Assert.Equal(
            "20260701120001|0|OK",
            Assert.Single(signature.VerifiedTexts));
    }

    [Fact]
    public async Task EchoPostAsync_RejectsInvalidGatewaySignature()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = "OK",
                signature = "invalid-signature"
            });
        var signature = new RecordingSignature
        {
            VerificationResult = false
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
            () => client.EchoPostAsync());

        Assert.Contains(
            "podpis",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "20260701120001|0|OK",
            Assert.Single(signature.VerifiedTexts));
    }

    [Fact]
    public async Task EchoPostAsync_RejectsStaleSignedResponse()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                dttm = "20260701114959",
                resultCode = 0,
                resultMessage = "OK",
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
            () => client.EchoPostAsync());

        Assert.Contains(
            "časové okno",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(signature.VerifiedTexts);
    }

    [Fact]
    public async Task EchoPostAsync_NonSuccessResponseIsNeverTreatedAsSigned()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(
                    "{\"resultCode\":180,\"resultMessage\":\"Not allowed\"}",
                    Encoding.UTF8,
                    "application/json")
            });
        var signature = new RecordingSignature
        {
            VerificationResult = true
        };
        var client = CreateClient(handler, signature);

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.EchoPostAsync());

        Assert.Equal(HttpStatusCode.Forbidden, exception.HttpStatusCode);
        Assert.Empty(signature.VerifiedTexts);
    }

    [Fact]
    public async Task EchoPostAsync_TimeoutAndCallerCancellationKeepExistingSemantics()
    {
        var timeoutClient = CreateClient(
            new ThrowingHandler(new TaskCanceledException()),
            new RecordingSignature());

        var timeout = await Assert.ThrowsAsync<CsobGatewayException>(
            () => timeoutClient.EchoPostAsync());

        Assert.IsType<TaskCanceledException>(timeout.InnerException);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledClient = CreateClient(
            new ThrowingHandler(new TaskCanceledException()),
            new RecordingSignature());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelledClient.EchoPostAsync(cancellation.Token));
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
    public async Task GetStatusAsync_ReturnsSignedAuthoritativeExpiry()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm = "20260701120001",
                resultCode = 130,
                resultMessage = "Payment expired",
                paymentStatus = 6,
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

        var result = await client.GetStatusAsync("ff41e84b7e33@HA");

        Assert.Equal("ff41e84b7e33@HA", result.PayId);
        Assert.Equal(130, result.ResultCode);
        Assert.Equal(6, result.PaymentStatus);
        Assert.Equal(
            "ff41e84b7e33@HA|20260701120001|130|Payment expired|6",
            Assert.Single(signature.VerifiedTexts));
    }

    [Fact]
    public async Task ReverseAsync_UsesOfficialPutContractAndVerifiesResponse()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 5,
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

        var result = await client.ReverseAsync("ff41e84b7e33@HA");

        Assert.Equal("ff41e84b7e33@HA", result.PayId);
        Assert.Equal(0, result.ResultCode);
        Assert.Equal(5, result.PaymentStatus);
        Assert.Equal(HttpMethod.Put, handler.Request!.Method);
        Assert.Equal(
            "/api/v1.9/payment/reverse",
            handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal(
            "application/json",
            handler.Request.Content!.Headers.ContentType!.MediaType);
        using var request = JsonDocument.Parse(handler.RequestBody);
        var root = request.RootElement;
        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.Equal("M1MIPS0000", root.GetProperty("merchantId").GetString());
        Assert.Equal("ff41e84b7e33@HA", root.GetProperty("payId").GetString());
        Assert.Equal("20260701120000", root.GetProperty("dttm").GetString());
        Assert.Equal(
            "merchant-signature",
            root.GetProperty("signature").GetString());
        Assert.Equal(
            "M1MIPS0000|ff41e84b7e33@HA|20260701120000",
            Assert.Single(signature.SignedTexts));
        Assert.Equal(
            "ff41e84b7e33@HA|20260701120001|0|OK|5",
            Assert.Single(signature.VerifiedTexts));
    }

    [Theory]
    [InlineData(null, "20260701120001", "ff41e84b7e33@HA", false)]
    [InlineData("invalid", "20260701120001", "ff41e84b7e33@HA", false)]
    [InlineData("gateway-signature", "20260701114959", "ff41e84b7e33@HA", true)]
    [InlineData("gateway-signature", "20260701120001", "aa41e84b7e33", true)]
    public async Task ReverseAsync_RejectsUnsignedStaleOrWrongPaymentResponse(
        string? gatewaySignature,
        string dttm,
        string responsePayId,
        bool verificationResult)
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = responsePayId,
                dttm,
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 5,
                signature = gatewaySignature
            });
        var signature = new RecordingSignature
        {
            VerificationResult = verificationResult
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

        await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.ReverseAsync("ff41e84b7e33@HA"));

        Assert.Single(signature.VerifiedTexts);
    }

    [Fact]
    public async Task ReverseAsync_MalformedResponseFailsClosed()
    {
        var client = CreateClient(
            new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{not-json",
                    Encoding.UTF8,
                    "application/json")
            }),
            new RecordingSignature { VerificationResult = true });

        await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.ReverseAsync("ff41e84b7e33@HA"));
    }

    [Fact]
    public async Task ReverseAsync_NonSuccessResponseIsNeverTrusted()
    {
        var signature = new RecordingSignature
        {
            VerificationResult = true
        };
        var client = CreateClient(
            new RecordingHandler(
                new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent(
                        "{\"resultCode\":160,\"resultMessage\":\"Not reversible\"}",
                        Encoding.UTF8,
                        "application/json")
                }),
            signature);

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.ReverseAsync("ff41e84b7e33@HA"));

        Assert.Equal(HttpStatusCode.Conflict, exception.HttpStatusCode);
        Assert.Empty(signature.VerifiedTexts);
    }

    [Fact]
    public async Task ReverseAsync_TransportTimeoutAndCancellationFailClosed()
    {
        var transportClient = CreateClient(
            new ThrowingHandler(
                new HttpRequestException("network unavailable")),
            new RecordingSignature());
        var transport = await Assert.ThrowsAsync<CsobGatewayException>(
            () => transportClient.ReverseAsync("ff41e84b7e33@HA"));
        Assert.IsType<HttpRequestException>(transport.InnerException);

        var timeoutClient = CreateClient(
            new ThrowingHandler(new TaskCanceledException()),
            new RecordingSignature());
        var timeout = await Assert.ThrowsAsync<CsobGatewayException>(
            () => timeoutClient.ReverseAsync("ff41e84b7e33@HA"));
        Assert.IsType<TaskCanceledException>(timeout.InnerException);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledClient = CreateClient(
            new ThrowingHandler(new TaskCanceledException()),
            new RecordingSignature());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelledClient.ReverseAsync(
                "ff41e84b7e33@HA",
                cancellation.Token));
    }

    [Fact]
    public async Task RefundAsync_FullRefundOmitsAmountAndVerifiesConditionalResponseFields()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 10,
                authCode = "F7A23E",
                statusDetail = "refunded",
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

        var result = await client.RefundAsync("ff41e84b7e33@HA");

        Assert.Equal("ff41e84b7e33@HA", result.PayId);
        Assert.Equal(0, result.ResultCode);
        Assert.Equal("OK", result.ResultMessage);
        Assert.Equal(10, result.PaymentStatus);
        Assert.Equal("F7A23E", result.AuthCode);
        Assert.Equal("refunded", result.StatusDetail);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(HttpMethod.Put, handler.Request!.Method);
        Assert.Equal(
            "/api/v1.9/payment/refund",
            handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal(
            "application/json",
            handler.Request.Content!.Headers.ContentType!.MediaType);
        using var request = JsonDocument.Parse(handler.RequestBody);
        var root = request.RootElement;
        Assert.Equal(4, root.EnumerateObject().Count());
        Assert.Equal("M1MIPS0000", root.GetProperty("merchantId").GetString());
        Assert.Equal("ff41e84b7e33@HA", root.GetProperty("payId").GetString());
        Assert.Equal("20260701120000", root.GetProperty("dttm").GetString());
        Assert.False(root.TryGetProperty("amount", out _));
        Assert.Equal(
            "merchant-signature",
            root.GetProperty("signature").GetString());
        Assert.Equal(
            "M1MIPS0000|ff41e84b7e33@HA|20260701120000",
            Assert.Single(signature.SignedTexts));
        Assert.Equal(
            "ff41e84b7e33@HA|20260701120001|0|OK|10|F7A23E|refunded",
            Assert.Single(signature.VerifiedTexts));
    }

    [Fact]
    public async Task RefundAsync_PartialRefundUsesExactInvariantMinorUnits()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 9,
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
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
            await client.RefundAsync("ff41e84b7e33@HA", 123456);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        using var request = JsonDocument.Parse(handler.RequestBody);
        var root = request.RootElement;
        Assert.Equal(5, root.EnumerateObject().Count());
        Assert.Equal(123456, root.GetProperty("amount").GetInt64());
        Assert.Equal(
            "M1MIPS0000|ff41e84b7e33@HA|20260701120000|123456",
            Assert.Single(signature.SignedTexts));
        Assert.Equal(
            "ff41e84b7e33@HA|20260701120001|0|OK|9",
            Assert.Single(signature.VerifiedTexts));
        Assert.DoesNotContain(
            "||",
            signature.SignedTexts[0],
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "||",
            signature.VerifiedTexts[0],
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RefundAsync_RejectsNonPositiveAmountBeforeHttp(long amount)
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK));
        var signature = new RecordingSignature();
        var client = CreateClient(handler, signature);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.RefundAsync("ff41e84b7e33@HA", amount));

        Assert.Null(handler.Request);
        Assert.Equal(0, handler.RequestCount);
        Assert.Empty(signature.SignedTexts);
    }

    [Fact]
    public async Task RefundAsync_RejectsInvalidPayIdBeforeHttp()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK));
        var signature = new RecordingSignature();
        var client = CreateClient(handler, signature);

        await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.RefundAsync("1234567890123456"));

        Assert.Null(handler.Request);
        Assert.Equal(0, handler.RequestCount);
        Assert.Empty(signature.SignedTexts);
    }

    [Fact]
    public async Task RefundAsync_DisabledGatewayFailsBeforeHttp()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK));
        var signature = new RecordingSignature();
        var client = CreateClient(
            handler,
            signature,
            isAvailable: false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.RefundAsync("ff41e84b7e33@HA"));

        Assert.Null(handler.Request);
        Assert.Equal(0, handler.RequestCount);
        Assert.Empty(signature.SignedTexts);
    }

    [Fact]
    public async Task RefundAsync_ReturnsSignedNonzeroResultWithoutBusinessMapping()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm = "20260701120001",
                resultCode = 160,
                resultMessage = "Payment state does not allow refund",
                paymentStatus = 8,
                statusDetail = "not refundable",
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

        var result = await client.RefundAsync("ff41e84b7e33@HA");

        Assert.Equal(160, result.ResultCode);
        Assert.Equal(8, result.PaymentStatus);
        Assert.Null(result.AuthCode);
        Assert.Equal("not refundable", result.StatusDetail);
        Assert.Equal(
            "ff41e84b7e33@HA|20260701120001|160|" +
            "Payment state does not allow refund|8|not refundable",
            Assert.Single(signature.VerifiedTexts));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-valid-signature")]
    public async Task RefundAsync_RejectsUnsignedOrInvalidlySignedResponse(
        string? gatewaySignature)
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 10,
                signature = gatewaySignature
            });
        var signature = new RecordingSignature
        {
            VerificationResult = false
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

        await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.RefundAsync("ff41e84b7e33@HA"));

        Assert.Single(signature.VerifiedTexts);
    }

    [Fact]
    public async Task RefundAsync_RejectsCryptographicallyTamperedResponse()
    {
        using var cryptography = new CryptographicGatewayFixture();
        var gatewaySignature = cryptography.SignGateway(
            "ff41e84b7e33@HA|20260701120001|0|OK|10|F7A23E");
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 9,
                authCode = "F7A23E",
                signature = gatewaySignature
            });
        var client = CreateClient(
            new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            }),
            cryptography.Signature);

        await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.RefundAsync("ff41e84b7e33@HA"));
    }

    [Theory]
    [InlineData("20260701114959")]
    [InlineData("20260701121001")]
    public async Task RefundAsync_RejectsStaleOrFutureSignedResponse(
        string dttm)
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm,
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 10,
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

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.RefundAsync("ff41e84b7e33@HA"));

        Assert.Contains(
            "časové okno",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefundAsync_RejectsSignedResponseForDifferentPayId()
    {
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "aa41e84b7e33",
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 10,
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

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.RefundAsync("ff41e84b7e33@HA"));

        Assert.Contains(
            "jiné platbě",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefundAsync_RejectsMalformedJson()
    {
        var client = CreateClient(
            new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{not-json",
                    Encoding.UTF8,
                    "application/json")
            }),
            new RecordingSignature { VerificationResult = true });

        await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.RefundAsync("ff41e84b7e33@HA"));
    }

    [Fact]
    public async Task RefundAsync_RejectsCryptographicallyValidResponseMissingRequiredStatus()
    {
        using var cryptography = new CryptographicGatewayFixture();
        var gatewaySignature = cryptography.SignGateway(
            "ff41e84b7e33@HA|20260701120001|0|OK");
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = "OK",
                signature = gatewaySignature
            });
        var client = CreateClient(
            new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            }),
            cryptography.Signature);

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.RefundAsync("ff41e84b7e33@HA"));

        Assert.Contains(
            "stav platby",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefundAsync_RejectsResponseMissingRequiredResultCode()
    {
        using var cryptography = new CryptographicGatewayFixture();
        var gatewaySignature = cryptography.SignGateway(
            "ff41e84b7e33@HA|20260701120001|0|OK|10");
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm = "20260701120001",
                resultMessage = "OK",
                paymentStatus = 10,
                signature = gatewaySignature
            });
        var client = CreateClient(
            new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            }),
            cryptography.Signature);

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.RefundAsync("ff41e84b7e33@HA"));

        Assert.Contains(
            "JSON",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefundAsync_NonSuccessHttpResponseIsNeverTrusted()
    {
        var signature = new RecordingSignature
        {
            VerificationResult = true
        };
        var client = CreateClient(
            new RecordingHandler(
                new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent(
                        "{\"resultCode\":160,\"resultMessage\":\"Not refundable\"}",
                        Encoding.UTF8,
                        "application/json")
                }),
            signature);

        var exception = await Assert.ThrowsAsync<CsobGatewayException>(
            () => client.RefundAsync("ff41e84b7e33@HA"));

        Assert.Equal(HttpStatusCode.Conflict, exception.HttpStatusCode);
        Assert.Empty(signature.VerifiedTexts);
    }

    [Fact]
    public async Task RefundAsync_TransportFailuresNeverIssueSecondPut()
    {
        var networkHandler = new ThrowingHandler(
            new HttpRequestException("network unavailable"));
        var networkClient = CreateClient(
            networkHandler,
            new RecordingSignature());
        var network = await Assert.ThrowsAsync<CsobGatewayException>(
            () => networkClient.RefundAsync("ff41e84b7e33@HA"));
        Assert.IsType<HttpRequestException>(network.InnerException);
        Assert.Equal(1, networkHandler.RequestCount);

        var timeoutHandler = new ThrowingHandler(
            new TaskCanceledException());
        var timeoutClient = CreateClient(
            timeoutHandler,
            new RecordingSignature());
        var timeout = await Assert.ThrowsAsync<CsobGatewayException>(
            () => timeoutClient.RefundAsync("ff41e84b7e33@HA"));
        Assert.IsType<TaskCanceledException>(timeout.InnerException);
        Assert.Equal(1, timeoutHandler.RequestCount);

        using var cancellation = new CancellationTokenSource();
        var cancellationHandler = new CancellingHandler(cancellation);
        var cancelledClient = CreateClient(
            cancellationHandler,
            new RecordingSignature());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelledClient.RefundAsync(
                "ff41e84b7e33@HA",
                cancellationToken: cancellation.Token));
        Assert.Equal(1, cancellationHandler.RequestCount);
    }

    [Fact]
    public async Task RefundAsync_UsesRealRsaSha256ForRequestAndResponseContract()
    {
        using var cryptography = new CryptographicGatewayFixture();
        const string responseText =
            "ff41e84b7e33@HA|20260701120001|0|OK|10|F7A23E|refunded";
        var response = JsonSerializer.Serialize(
            new
            {
                payId = "ff41e84b7e33@HA",
                dttm = "20260701120001",
                resultCode = 0,
                resultMessage = "OK",
                paymentStatus = 10,
                authCode = "F7A23E",
                statusDetail = "refunded",
                signature = cryptography.SignGateway(responseText)
            });
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response,
                    Encoding.UTF8,
                    "application/json")
            });
        var client = CreateClient(handler, cryptography.Signature);

        var result = await client.RefundAsync(
            "ff41e84b7e33@HA",
            123456);

        Assert.Equal(0, result.ResultCode);
        using var request = JsonDocument.Parse(handler.RequestBody);
        var requestSignature = request.RootElement
            .GetProperty("signature")
            .GetString();
        Assert.True(cryptography.VerifyMerchant(
            "M1MIPS0000|ff41e84b7e33@HA|20260701120000|123456",
            requestSignature!));
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
        ICsobGatewaySignature signature,
        DateTimeOffset? currentTime = null,
        bool isAvailable = true)
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
            new CsobGatewayAvailability(isAvailable),
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

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromException<HttpResponseMessage>(_exception);
        }
    }

    private sealed class CancellingHandler : HttpMessageHandler
    {
        private readonly CancellationTokenSource _cancellation;

        public CancellingHandler(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            _cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(
                cancellationToken);
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

        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Request = request;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _response;
        }
    }

    private sealed class CryptographicGatewayFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"fua-pay-refund-crypto-{Guid.NewGuid():N}");
        private readonly RSA _merchant = RSA.Create(2048);
        private readonly RSA _gateway = RSA.Create(2048);

        public CryptographicGatewayFixture()
        {
            Directory.CreateDirectory(_directory);
            var merchantPrivateKeyPath = Path.Combine(
                _directory,
                "merchant-private.pem");
            var gatewayPublicKeyPath = Path.Combine(
                _directory,
                "gateway-public.pem");
            File.WriteAllText(
                merchantPrivateKeyPath,
                _merchant.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(
                gatewayPublicKeyPath,
                _gateway.ExportSubjectPublicKeyInfoPem());
            Signature = new CsobGatewaySignature(
                new CsobGatewayConfiguration(
                    Enabled: true,
                    CsobGatewayConfiguration.SandboxApiBaseUri,
                    "M1MIPS0000",
                    merchantPrivateKeyPath,
                    gatewayPublicKeyPath,
                    new Uri("https://shop.example.com/payments/csob/return"),
                    900,
                    TimeSpan.FromSeconds(30)));
        }

        public CsobGatewaySignature Signature { get; }

        public string SignGateway(string text) =>
            Convert.ToBase64String(
                _gateway.SignData(
                    Encoding.UTF8.GetBytes(text),
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1));

        public bool VerifyMerchant(string text, string signature) =>
            _merchant.VerifyData(
                Encoding.UTF8.GetBytes(text),
                Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

        public void Dispose()
        {
            Signature.Dispose();
            _merchant.Dispose();
            _gateway.Dispose();

            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
