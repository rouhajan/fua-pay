using FuaPay.Web.BuildingBlocks.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed class CreditAccountNotFoundException : InvalidOperationException
{
    public CreditAccountNotFoundException(Guid ownerId)
        : base($"Kreditní účet uživatele '{ownerId}' nebyl nalezen.")
    {
        OwnerId = ownerId;
    }

    public Guid OwnerId { get; }
}

public sealed class CreditAccountConcurrencyException :
    InvalidOperationException
{
    public CreditAccountConcurrencyException(
        Guid ownerId,
        Exception innerException)
        : base(
            $"Kreditní účet uživatele '{ownerId}' byl souběžně změněn.",
            innerException)
    {
        OwnerId = ownerId;
    }

    public Guid OwnerId { get; }
}

public sealed class CreditAdjustmentCommandAlreadyExistsException :
    InvalidOperationException
{
    public CreditAdjustmentCommandAlreadyExistsException(
        Guid commandId,
        Exception? innerException = null)
        : base(
            $"Příkaz korekce kreditu '{commandId}' již existuje.",
            innerException)
    {
        CommandId = commandId;
    }

    public Guid CommandId { get; }
}

public sealed class CreditAdjustmentCommandConflictException :
    InvalidOperationException
{
    public CreditAdjustmentCommandConflictException(Guid commandId)
        : base(
            $"Příkaz korekce kreditu '{commandId}' byl již použit s jinými daty.")
    {
        CommandId = commandId;
    }

    public Guid CommandId { get; }
}

public sealed class CreditAdjustmentAmountNotAllowedException :
    InvalidOperationException
{
    public CreditAdjustmentAmountNotAllowedException()
        : base("Částka administrativní korekce je mimo povolený rozsah.")
    {
    }
}

public sealed class CreditAdjustmentReasonNotAllowedException :
    InvalidOperationException
{
    public CreditAdjustmentReasonNotAllowedException()
        : base("Důvod administrativní korekce není platný.")
    {
    }
}

public sealed class InsufficientAvailablePrintCreditException :
    InvalidOperationException
{
    public InsufficientAvailablePrintCreditException(
        Guid ownerId,
        Money requested,
        Money available)
        : base(
            $"Credit account '{ownerId}' has {available.MinorUnits} " +
            $"minor units available, but {requested.MinorUnits} were requested.")
    {
        OwnerId = ownerId;
        Requested = requested;
        Available = available;
    }

    public Guid OwnerId { get; }

    public Money Requested { get; }

    public Money Available { get; }
}

public sealed class PrintReservationCommandConflictException :
    InvalidOperationException
{
    public PrintReservationCommandConflictException(
        Guid printSourceId,
        Guid reserveCommandId)
        : base(
            $"Print reservation command '{reserveCommandId}' for source " +
            $"'{printSourceId}' was already used with different data.")
    {
        PrintSourceId = printSourceId;
        ReserveCommandId = reserveCommandId;
    }

    public Guid PrintSourceId { get; }

    public Guid ReserveCommandId { get; }
}

public sealed class PrintReservationJobConflictException :
    InvalidOperationException
{
    public PrintReservationJobConflictException(
        Guid printSourceId,
        string jobUuid)
        : base(
            $"Print job '{jobUuid}' for source '{printSourceId}' already " +
            "has a reservation created by another command.")
    {
        PrintSourceId = printSourceId;
        JobUuid = jobUuid;
    }

    public Guid PrintSourceId { get; }

    public string JobUuid { get; }
}

public sealed class PrintReservationReserveCommandAlreadyExistsException :
    InvalidOperationException
{
    public PrintReservationReserveCommandAlreadyExistsException(
        Guid printSourceId,
        Guid reserveCommandId,
        Exception innerException)
        : base(
            $"Print reservation command '{reserveCommandId}' for source " +
            $"'{printSourceId}' already exists.",
            innerException)
    {
        PrintSourceId = printSourceId;
        ReserveCommandId = reserveCommandId;
    }

    public Guid PrintSourceId { get; }

    public Guid ReserveCommandId { get; }
}

public sealed class PrintReservationPrintJobAlreadyExistsException :
    InvalidOperationException
{
    public PrintReservationPrintJobAlreadyExistsException(
        Guid printSourceId,
        string jobUuid,
        Exception innerException)
        : base(
            $"Print job '{jobUuid}' for source '{printSourceId}' already " +
            "has a reservation.",
            innerException)
    {
        PrintSourceId = printSourceId;
        JobUuid = jobUuid;
    }

    public Guid PrintSourceId { get; }

    public string JobUuid { get; }
}

public sealed class PrintReservationNotFoundException :
    InvalidOperationException
{
    public PrintReservationNotFoundException(Guid reservationId)
        : base($"Print reservation '{reservationId}' was not found.")
    {
        ReservationId = reservationId;
    }

    public Guid ReservationId { get; }
}

public sealed class PrintReservationSourceConflictException :
    InvalidOperationException
{
    public PrintReservationSourceConflictException(
        Guid reservationId,
        Guid printSourceId)
        : base(
            $"Print reservation '{reservationId}' does not belong to " +
            $"print source '{printSourceId}'.")
    {
        ReservationId = reservationId;
        PrintSourceId = printSourceId;
    }

    public Guid ReservationId { get; }

    public Guid PrintSourceId { get; }
}

public sealed class PrintReservationResolutionCommandConflictException :
    InvalidOperationException
{
    public PrintReservationResolutionCommandConflictException(
        Guid printSourceId,
        Guid resolutionCommandId)
        : base(
            $"Print reservation resolution command '{resolutionCommandId}' " +
            $"for source '{printSourceId}' conflicts with persisted state.")
    {
        PrintSourceId = printSourceId;
        ResolutionCommandId = resolutionCommandId;
    }

    public Guid PrintSourceId { get; }

    public Guid ResolutionCommandId { get; }
}

public sealed class PrintReservationTerminalCommandConflictException :
    InvalidOperationException
{
    public PrintReservationTerminalCommandConflictException(
        Guid printSourceId,
        Guid terminalCommandId)
        : base(
            $"Print reservation terminal command '{terminalCommandId}' " +
            $"for source '{printSourceId}' conflicts with persisted state.")
    {
        PrintSourceId = printSourceId;
        TerminalCommandId = terminalCommandId;
    }

    public Guid PrintSourceId { get; }

    public Guid TerminalCommandId { get; }
}

public sealed class PrintReservationConcurrencyException :
    InvalidOperationException
{
    public PrintReservationConcurrencyException(
        Guid reservationId,
        Exception innerException)
        : base(
            $"Print reservation '{reservationId}' was changed concurrently.",
            innerException)
    {
        ReservationId = reservationId;
    }

    public Guid ReservationId { get; }
}

public sealed class PrintReservationResolutionCommandAlreadyExistsException :
    InvalidOperationException
{
    public PrintReservationResolutionCommandAlreadyExistsException(
        Guid printSourceId,
        Guid resolutionCommandId,
        Exception innerException)
        : base(
            $"Print reservation resolution command '{resolutionCommandId}' " +
            $"for source '{printSourceId}' already exists.",
            innerException)
    {
        PrintSourceId = printSourceId;
        ResolutionCommandId = resolutionCommandId;
    }

    public Guid PrintSourceId { get; }

    public Guid ResolutionCommandId { get; }
}

public sealed class PrintReservationTerminalCommandAlreadyExistsException :
    InvalidOperationException
{
    public PrintReservationTerminalCommandAlreadyExistsException(
        Guid printSourceId,
        Guid terminalCommandId,
        Exception innerException)
        : base(
            $"Print reservation terminal command '{terminalCommandId}' " +
            $"for source '{printSourceId}' already exists.",
            innerException)
    {
        PrintSourceId = printSourceId;
        TerminalCommandId = terminalCommandId;
    }

    public Guid PrintSourceId { get; }

    public Guid TerminalCommandId { get; }
}
