using System.Text;

using FuaPay.Web.Modules.Payments.Application;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public static class CsobPaymentReturnEndpoint
{
    private const string Route = "/payments/csob/return";
    internal const string RateLimitPolicy = "csob-payment-return";
    public const int MaximumRequestBodyBytes = 4 * 1024;

    public static IEndpointConventionBuilder MapCsobPaymentReturn(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapMethods(
                Route,
                [HttpMethods.Get, HttpMethods.Post],
                (
                    HttpContext context,
                    ICsobPaymentRecoveryScheduler scheduler,
                    CancellationToken cancellationToken) =>
                    HandleAsync(
                        context,
                        scheduler,
                        cancellationToken))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy);
    }

    public static async Task HandleAsync(
        HttpContext context,
        ICsobPaymentRecoveryScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scheduler);

        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Pragma"] = "no-cache";

        string? payId;

        if (HttpMethods.IsPost(context.Request.Method))
        {
            if (!HasSupportedFormContentType(context.Request.ContentType))
            {
                context.Response.StatusCode =
                    StatusCodes.Status415UnsupportedMediaType;
                return;
            }

            if (
                context.Request.ContentLength is >
                    MaximumRequestBodyBytes)
            {
                context.Response.StatusCode =
                    StatusCodes.Status413PayloadTooLarge;
                return;
            }

            var maxBodySize = context.Features
                .Get<IHttpMaxRequestBodySizeFeature>();

            if (maxBodySize is { IsReadOnly: false })
            {
                maxBodySize.MaxRequestBodySize = MaximumRequestBodyBytes;
            }

            try
            {
                var body = await ReadBoundedBodyAsync(
                    context.Request.Body,
                    MaximumRequestBodyBytes,
                    cancellationToken);
                var form = QueryHelpers.ParseQuery(
                    Encoding.UTF8.GetString(body));
                payId = form["payId"].FirstOrDefault();
            }
            catch (RequestBodyTooLargeException)
            {
                context.Response.StatusCode =
                    StatusCodes.Status413PayloadTooLarge;
                return;
            }
            catch (BadHttpRequestException exception)
                when (
                    exception.StatusCode ==
                        StatusCodes.Status413PayloadTooLarge)
            {
                context.Response.StatusCode =
                    StatusCodes.Status413PayloadTooLarge;
                return;
            }
            catch (Exception exception)
                when (exception is InvalidDataException or FormatException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
        }
        else
        {
            payId = context.Request.Query["payId"].FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(payId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        Guid paymentId;

        try
        {
            paymentId = await scheduler.ScheduleReturnAsync(
                payId,
                cancellationToken);
        }
        catch (Exception exception)
            when (
                exception is ArgumentException or
                PaymentProviderReferenceNotFoundException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var pathBase = context.Request.PathBase.HasValue
            ? context.Request.PathBase.Value
            : string.Empty;
        var location =
            $"{pathBase}/Customer/Payments/Details" +
            $"?id={paymentId:D}&view=customer";

        context.Response.StatusCode = StatusCodes.Status303SeeOther;
        context.Response.Headers.Location = location;
    }

    private static bool HasSupportedFormContentType(string? contentType)
    {
        return
            MediaTypeHeaderValue.TryParse(
                contentType,
                out var parsed) &&
            parsed.MediaType.Equals(
                "application/x-www-form-urlencoded",
                StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadBoundedBodyAsync(
        Stream body,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[maximumBytes + 1];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await body.ReadAsync(
                buffer.AsMemory(total, buffer.Length - total),
                cancellationToken);

            if (read == 0)
            {
                return buffer[..total];
            }

            total += read;
        }

        throw new RequestBodyTooLargeException();
    }

    private sealed class RequestBodyTooLargeException : Exception;
}
