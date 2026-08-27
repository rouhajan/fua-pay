using System.Text.Json;
using System.Text.Json.Serialization;

using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Infrastructure.Entra;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

using Microsoft.AspNetCore.RateLimiting;

namespace FuaPay.Web.Modules.Credits.Web.PrintPayments;

public static class PrintPaymentsEndpoint
{
    private const int MaximumRequestBodyBytes = 8 * 1024;

    private static readonly JsonSerializerOptions RequestJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

    public static IEndpointRouteBuilder MapPrintPaymentsApi(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup("/api/print-payments")
            .RequireAuthorization(
                FuaPrintAuthenticationDefaults.AuthorizationPolicy)
            .RequireRateLimiting(
                FuaPrintAuthenticationDefaults.RateLimitPolicy)
            .DisableAntiforgery();

        group.MapPost("/reservations", ReserveAsync);
        group.MapGet("/reservations", FindByJobAsync);
        group.MapPost(
            "/reservations/{reservationId}/resolution-required",
            RequireResolutionAsync);
        group.MapPost(
            "/reservations/{reservationId}/capture",
            CaptureAsync);
        group.MapPost(
            "/reservations/{reservationId}/release",
            ReleaseAsync);

        return endpoints;
    }

    private static async Task<IResult> ReserveAsync(
        HttpContext context,
        LinkedIdentityResolver identityResolver,
        PrintReservationService reservationService)
    {
        var body = await ReadBodyAsync<ReservePrintPaymentRequest>(
            context);

        if (!body.IsValid || body.Value is null)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_request");
        }

        var request = body.Value;

        if (request.ReserveCommandId == Guid.Empty)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_request");
        }

        string jobUuid;

        try
        {
            jobUuid = IppJobUuid.Normalize(request.JobUuid!);
        }
        catch (ArgumentException)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_job_uuid");
        }

        if (request.AmountMinorUnits <= 0)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_amount");
        }

        if (!string.Equals(
                request.Currency,
                "CZK",
                StringComparison.Ordinal))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "unsupported_currency");
        }

        if (!TryParseIdentity(
                request.UserIdentity,
                out var tenantId,
                out var objectId))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_identity");
        }

        Guid ownerId;

        try
        {
            ownerId = await identityResolver.ResolveMicrosoftEntraAsync(
                tenantId,
                objectId,
                context.RequestAborted);
        }
        catch (LinkedIdentityNotFoundException)
        {
            return Problem(
                StatusCodes.Status404NotFound,
                "identity_not_linked");
        }
        catch (LinkedIdentityNotEligibleException)
        {
            return Problem(
                StatusCodes.Status403Forbidden,
                "user_not_eligible");
        }

        try
        {
            var reservation = await reservationService.ReserveAsync(
                new ReservePrintCreditCommand(
                    ownerId,
                    context.User.GetRequiredPrintSourceId(),
                    jobUuid,
                    new Money(request.AmountMinorUnits),
                    request.ReserveCommandId),
                context.RequestAborted);

            return Results.Ok(ToResponse(reservation));
        }
        catch (CreditAccountNotFoundException)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "insufficient_credit");
        }
        catch (InsufficientAvailablePrintCreditException)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "insufficient_credit");
        }
        catch (PrintReservationCommandConflictException)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "idempotency_conflict");
        }
        catch (PrintReservationJobConflictException)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "print_job_conflict");
        }
    }

    private static async Task<IResult> FindByJobAsync(
        HttpContext context,
        PrintReservationService reservationService)
    {
        var values = context.Request.Query["jobUuid"];

        if (context.Request.Query.Count != 1)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_request");
        }

        if (values.Count != 1)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_job_uuid");
        }

        string jobUuid;

        try
        {
            jobUuid = IppJobUuid.Normalize(values[0]!);
        }
        catch (ArgumentException)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_job_uuid");
        }

        var reservation =
            await reservationService.FindByPrintJobAsync(
                context.User.GetRequiredPrintSourceId(),
                jobUuid,
                context.RequestAborted);

        return reservation is null
            ? Problem(
                StatusCodes.Status404NotFound,
                "reservation_not_found")
            : Results.Ok(ToResponse(reservation));
    }

    private static async Task<IResult> RequireResolutionAsync(
        string reservationId,
        HttpContext context,
        PrintReservationService reservationService)
    {
        if (!TryParseReservationId(reservationId, out var parsedId))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_request");
        }

        var body = await ReadBodyAsync<ResolutionRequiredRequest>(
            context);

        if (
            !body.IsValid ||
            body.Value is null ||
            body.Value.ResolutionCommandId == Guid.Empty)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_request");
        }

        try
        {
            var reservation =
                await reservationService.RequireResolutionAsync(
                    new RequirePrintReservationResolutionCommand(
                        parsedId,
                        context.User.GetRequiredPrintSourceId(),
                        body.Value.ResolutionCommandId),
                    context.RequestAborted);

            return Results.Ok(ToResponse(reservation));
        }
        catch (PrintReservationNotFoundException)
        {
            return ReservationNotFound();
        }
        catch (PrintReservationSourceConflictException)
        {
            return ReservationNotFound();
        }
        catch (PrintReservationResolutionCommandConflictException)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "reservation_conflict");
        }
        catch (InvalidPrintReservationStateTransitionException)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "invalid_lifecycle_transition");
        }
    }

    private static async Task<IResult> CaptureAsync(
        string reservationId,
        HttpContext context,
        PrintReservationService reservationService)
    {
        if (!TryParseReservationId(reservationId, out var parsedId))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_request");
        }

        var body = await ReadBodyAsync<TerminalPrintPaymentRequest>(
            context);

        if (
            !body.IsValid ||
            body.Value is null ||
            body.Value.TerminalCommandId == Guid.Empty)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_request");
        }

        try
        {
            var reservation = await reservationService.CaptureAsync(
                new CapturePrintReservationCommand(
                    parsedId,
                    context.User.GetRequiredPrintSourceId(),
                    body.Value.TerminalCommandId),
                context.RequestAborted);

            return Results.Ok(ToResponse(reservation));
        }
        catch (PrintReservationNotFoundException)
        {
            return ReservationNotFound();
        }
        catch (PrintReservationSourceConflictException)
        {
            return ReservationNotFound();
        }
        catch (PrintReservationTerminalCommandConflictException)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "reservation_conflict");
        }
        catch (InvalidPrintReservationStateTransitionException)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "invalid_lifecycle_transition");
        }
        catch (InsufficientCreditException)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "insufficient_credit");
        }
    }

    private static async Task<IResult> ReleaseAsync(
        string reservationId,
        HttpContext context,
        PrintReservationService reservationService)
    {
        if (!TryParseReservationId(reservationId, out var parsedId))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_request");
        }

        var body = await ReadBodyAsync<TerminalPrintPaymentRequest>(
            context);

        if (
            !body.IsValid ||
            body.Value is null ||
            body.Value.TerminalCommandId == Guid.Empty)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_request");
        }

        try
        {
            var reservation = await reservationService.ReleaseAsync(
                new ReleasePrintReservationCommand(
                    parsedId,
                    context.User.GetRequiredPrintSourceId(),
                    body.Value.TerminalCommandId),
                context.RequestAborted);

            return Results.Ok(ToResponse(reservation));
        }
        catch (PrintReservationNotFoundException)
        {
            return ReservationNotFound();
        }
        catch (PrintReservationSourceConflictException)
        {
            return ReservationNotFound();
        }
        catch (PrintReservationTerminalCommandConflictException)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "reservation_conflict");
        }
        catch (InvalidPrintReservationStateTransitionException)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                "invalid_lifecycle_transition");
        }
    }

    private static bool TryParseIdentity(
        PrintPaymentUserIdentityRequest? identity,
        out Guid tenantId,
        out Guid objectId)
    {
        tenantId = Guid.Empty;
        objectId = Guid.Empty;

        return
            identity is not null &&
            string.Equals(
                identity.Provider,
                EntraAuthenticationDefaults.ExternalIdentityProvider,
                StringComparison.Ordinal) &&
            Guid.TryParse(identity.TenantId, out tenantId) &&
            tenantId != Guid.Empty &&
            Guid.TryParse(identity.ObjectId, out objectId) &&
            objectId != Guid.Empty;
    }

    private static bool TryParseReservationId(
        string value,
        out Guid reservationId)
    {
        return
            Guid.TryParse(value, out reservationId) &&
            reservationId != Guid.Empty;
    }

    private static async Task<BodyReadResult<T>> ReadBodyAsync<T>(
        HttpContext context)
        where T : class
    {
        if (
            !context.Request.HasJsonContentType() ||
            context.Request.ContentLength > MaximumRequestBodyBytes)
        {
            return new BodyReadResult<T>(false, null);
        }

        try
        {
            var buffer = new byte[MaximumRequestBodyBytes + 1];
            var bytesRead = 0;

            while (bytesRead < buffer.Length)
            {
                var read = await context.Request.Body.ReadAsync(
                    buffer.AsMemory(bytesRead),
                    context.RequestAborted);

                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
            }

            if (bytesRead > MaximumRequestBodyBytes)
            {
                return new BodyReadResult<T>(false, null);
            }

            var value = JsonSerializer.Deserialize<T>(
                buffer.AsSpan(0, bytesRead),
                RequestJsonOptions);

            return new BodyReadResult<T>(value is not null, value);
        }
        catch (JsonException)
        {
            return new BodyReadResult<T>(false, null);
        }
        catch (NotSupportedException)
        {
            return new BodyReadResult<T>(false, null);
        }
    }

    private static PrintPaymentReservationResponse ToResponse(
        PrintReservationResult reservation)
    {
        return new PrintPaymentReservationResponse(
            reservation.Id,
            reservation.JobUuid,
            reservation.Amount.MinorUnits,
            "CZK",
            reservation.Status.ToString(),
            reservation.ReserveCommandId,
            reservation.ResolutionCommandId,
            reservation.TerminalCommandId,
            reservation.DebitOperationId,
            reservation.CreatedAt,
            reservation.StateChangedAt);
    }

    private static IResult ReservationNotFound() =>
        Problem(
            StatusCodes.Status404NotFound,
            "reservation_not_found");

    private static IResult Problem(int statusCode, string code)
    {
        return Results.Problem(
            statusCode: statusCode,
            title: Title(code),
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });
    }

    private static string Title(string code) =>
        code switch
        {
            "invalid_request" => "The request is invalid.",
            "invalid_job_uuid" => "The print job UUID is invalid.",
            "invalid_amount" => "The amount is invalid.",
            "unsupported_currency" => "The currency is unsupported.",
            "invalid_identity" => "The user identity is invalid.",
            "identity_not_linked" => "The identity is not linked.",
            "user_not_eligible" => "The user is not eligible.",
            "reservation_not_found" => "The reservation was not found.",
            "insufficient_credit" => "Available credit is insufficient.",
            "idempotency_conflict" => "The command conflicts with persisted data.",
            "print_job_conflict" => "The print job already has another reservation.",
            "reservation_conflict" => "The reservation command conflicts with persisted state.",
            "invalid_lifecycle_transition" => "The lifecycle transition is invalid.",
            _ => throw new ArgumentOutOfRangeException(nameof(code))
        };

    private sealed record BodyReadResult<T>(bool IsValid, T? Value)
        where T : class;
}
