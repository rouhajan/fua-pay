using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed record ReservePrintCreditCommand
{
    public ReservePrintCreditCommand(
        Guid ownerId,
        Guid printSourceId,
        string jobUuid,
        Money amount,
        Guid reserveCommandId)
    {
        ValidateId(ownerId, nameof(ownerId));
        ValidateId(printSourceId, nameof(printSourceId));
        ValidateId(reserveCommandId, nameof(reserveCommandId));

        if (amount.MinorUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "The reservation amount must be positive.");
        }

        OwnerId = ownerId;
        PrintSourceId = printSourceId;
        JobUuid = IppJobUuid.Normalize(jobUuid);
        Amount = amount;
        ReserveCommandId = reserveCommandId;
    }

    public Guid OwnerId { get; }

    public Guid PrintSourceId { get; }

    public string JobUuid { get; }

    public Money Amount { get; }

    public Guid ReserveCommandId { get; }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "The reservation identifier must not be empty.",
                parameterName);
        }
    }
}

public sealed record PrintReservationResult(
    Guid Id,
    Guid CreditAccountId,
    Guid PrintSourceId,
    string JobUuid,
    Money Amount,
    PrintReservationStatus Status,
    Guid ReserveCommandId,
    Guid? ResolutionCommandId,
    Guid? TerminalCommandId,
    Guid? DebitOperationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset StateChangedAt,
    long Version);

public sealed record RequirePrintReservationResolutionCommand
{
    public RequirePrintReservationResolutionCommand(
        Guid reservationId,
        Guid printSourceId,
        Guid resolutionCommandId)
    {
        PrintReservationCommandValidation.ValidateId(
            reservationId,
            nameof(reservationId));
        PrintReservationCommandValidation.ValidateId(
            printSourceId,
            nameof(printSourceId));
        PrintReservationCommandValidation.ValidateId(
            resolutionCommandId,
            nameof(resolutionCommandId));

        ReservationId = reservationId;
        PrintSourceId = printSourceId;
        ResolutionCommandId = resolutionCommandId;
    }

    public Guid ReservationId { get; }

    public Guid PrintSourceId { get; }

    public Guid ResolutionCommandId { get; }
}

public sealed record CapturePrintReservationCommand
{
    public CapturePrintReservationCommand(
        Guid reservationId,
        Guid printSourceId,
        Guid terminalCommandId)
    {
        PrintReservationCommandValidation.ValidateId(
            reservationId,
            nameof(reservationId));
        PrintReservationCommandValidation.ValidateId(
            printSourceId,
            nameof(printSourceId));
        PrintReservationCommandValidation.ValidateId(
            terminalCommandId,
            nameof(terminalCommandId));

        ReservationId = reservationId;
        PrintSourceId = printSourceId;
        TerminalCommandId = terminalCommandId;
    }

    public Guid ReservationId { get; }

    public Guid PrintSourceId { get; }

    public Guid TerminalCommandId { get; }
}

public sealed record ReleasePrintReservationCommand
{
    public ReleasePrintReservationCommand(
        Guid reservationId,
        Guid printSourceId,
        Guid terminalCommandId)
    {
        PrintReservationCommandValidation.ValidateId(
            reservationId,
            nameof(reservationId));
        PrintReservationCommandValidation.ValidateId(
            printSourceId,
            nameof(printSourceId));
        PrintReservationCommandValidation.ValidateId(
            terminalCommandId,
            nameof(terminalCommandId));

        ReservationId = reservationId;
        PrintSourceId = printSourceId;
        TerminalCommandId = terminalCommandId;
    }

    public Guid ReservationId { get; }

    public Guid PrintSourceId { get; }

    public Guid TerminalCommandId { get; }
}

internal static class PrintReservationCommandValidation
{
    internal static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "The reservation command identifier must not be empty.",
                parameterName);
        }
    }
}
