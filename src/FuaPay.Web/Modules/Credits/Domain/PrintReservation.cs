using FuaPay.Web.BuildingBlocks.Domain;

namespace FuaPay.Web.Modules.Credits.Domain;

public sealed class PrintReservation
{
    public PrintReservation(
        Guid id,
        Guid creditAccountId,
        Guid printSourceId,
        string jobUuid,
        Money amount,
        Guid reserveCommandId,
        DateTimeOffset createdAt)
    {
        ValidateId(
            id,
            nameof(id),
            "The reservation ID must not be empty.");
        ValidateId(
            creditAccountId,
            nameof(creditAccountId),
            "The credit account ID must not be empty.");
        ValidateId(
            printSourceId,
            nameof(printSourceId),
            "The print source ID must not be empty.");
        ValidateId(
            reserveCommandId,
            nameof(reserveCommandId),
            "The reserve command ID must not be empty.");

        if (amount.MinorUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "The reservation amount must be positive.");
        }

        if (createdAt == default)
        {
            throw new ArgumentException(
                "The reservation creation time must not be empty.",
                nameof(createdAt));
        }

        Id = id;
        CreditAccountId = creditAccountId;
        PrintSourceId = printSourceId;
        JobUuid = IppJobUuid.Normalize(jobUuid);
        Amount = amount;
        Status = PrintReservationStatus.Reserved;
        ReserveCommandId = reserveCommandId;
        ResolutionCommandId = null;
        TerminalCommandId = null;
        DebitOperationId = null;
        CreatedAt = createdAt;
        StateChangedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid CreditAccountId { get; }

    public Guid PrintSourceId { get; }

    public string JobUuid { get; }

    public Money Amount { get; }

    public PrintReservationStatus Status { get; private set; }

    public Guid ReserveCommandId { get; }

    public Guid? ResolutionCommandId { get; private set; }

    public Guid? TerminalCommandId { get; private set; }

    public Guid? DebitOperationId { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset StateChangedAt { get; private set; }

    public bool RequireResolution(
        Guid resolutionCommandId,
        DateTimeOffset changedAt)
    {
        ValidateId(
            resolutionCommandId,
            nameof(resolutionCommandId),
            "The resolution command ID must not be empty.");
        ValidateTransitionTime(changedAt);

        if (
            Status == PrintReservationStatus.ResolutionRequired &&
            ResolutionCommandId == resolutionCommandId)
        {
            return false;
        }

        EnsureTransitionAllowed(
            PrintReservationStatus.Reserved,
            PrintReservationStatus.ResolutionRequired);

        Status = PrintReservationStatus.ResolutionRequired;
        ResolutionCommandId = resolutionCommandId;
        StateChangedAt = changedAt;
        return true;
    }

    public bool Capture(
        Guid terminalCommandId,
        Guid debitOperationId,
        DateTimeOffset changedAt)
    {
        ValidateId(
            terminalCommandId,
            nameof(terminalCommandId),
            "The terminal command ID must not be empty.");
        ValidateId(
            debitOperationId,
            nameof(debitOperationId),
            "The debit operation ID must not be empty.");
        ValidateTransitionTime(changedAt);

        if (
            Status == PrintReservationStatus.Captured &&
            TerminalCommandId == terminalCommandId &&
            DebitOperationId == debitOperationId)
        {
            return false;
        }

        EnsureBlockingTransitionAllowed(
            PrintReservationStatus.Captured);

        Status = PrintReservationStatus.Captured;
        TerminalCommandId = terminalCommandId;
        DebitOperationId = debitOperationId;
        StateChangedAt = changedAt;
        return true;
    }

    public bool Release(
        Guid terminalCommandId,
        DateTimeOffset changedAt)
    {
        ValidateId(
            terminalCommandId,
            nameof(terminalCommandId),
            "The terminal command ID must not be empty.");
        ValidateTransitionTime(changedAt);

        if (
            Status == PrintReservationStatus.Released &&
            TerminalCommandId == terminalCommandId)
        {
            return false;
        }

        EnsureBlockingTransitionAllowed(
            PrintReservationStatus.Released);

        Status = PrintReservationStatus.Released;
        TerminalCommandId = terminalCommandId;
        DebitOperationId = null;
        StateChangedAt = changedAt;
        return true;
    }

    internal static PrintReservation Restore(
        Guid id,
        Guid creditAccountId,
        Guid printSourceId,
        string jobUuid,
        Money amount,
        PrintReservationStatus status,
        Guid reserveCommandId,
        Guid? resolutionCommandId,
        Guid? terminalCommandId,
        Guid? debitOperationId,
        DateTimeOffset createdAt,
        DateTimeOffset stateChangedAt)
    {
        var reservation = new PrintReservation(
            id,
            creditAccountId,
            printSourceId,
            jobUuid,
            amount,
            reserveCommandId,
            createdAt)
        {
            Status = status,
            ResolutionCommandId = resolutionCommandId,
            TerminalCommandId = terminalCommandId,
            DebitOperationId = debitOperationId,
            StateChangedAt = stateChangedAt
        };

        reservation.ValidatePersistedState();
        return reservation;
    }

    private void EnsureBlockingTransitionAllowed(
        PrintReservationStatus targetStatus)
    {
        if (
            Status is not PrintReservationStatus.Reserved and
                not PrintReservationStatus.ResolutionRequired)
        {
            throw new InvalidPrintReservationStateTransitionException(
                Status,
                targetStatus);
        }
    }

    private void EnsureTransitionAllowed(
        PrintReservationStatus sourceStatus,
        PrintReservationStatus targetStatus)
    {
        if (Status != sourceStatus)
        {
            throw new InvalidPrintReservationStateTransitionException(
                Status,
                targetStatus);
        }
    }

    private void ValidateTransitionTime(DateTimeOffset changedAt)
    {
        if (changedAt == default || changedAt < StateChangedAt)
        {
            throw new ArgumentException(
                "The reservation transition time must be monotonic.",
                nameof(changedAt));
        }
    }

    private void ValidatePersistedState()
    {
        if (StateChangedAt < CreatedAt)
        {
            throw new InvalidDataException(
                $"Print reservation '{Id}' has invalid timestamps.");
        }

        ValidateOptionalId(
            ResolutionCommandId,
            nameof(ResolutionCommandId));
        ValidateOptionalId(
            TerminalCommandId,
            nameof(TerminalCommandId));
        ValidateOptionalId(
            DebitOperationId,
            nameof(DebitOperationId));

        var validShape = Status switch
        {
            PrintReservationStatus.Reserved =>
                ResolutionCommandId is null &&
                TerminalCommandId is null &&
                DebitOperationId is null,
            PrintReservationStatus.ResolutionRequired =>
                ResolutionCommandId.HasValue &&
                TerminalCommandId is null &&
                DebitOperationId is null,
            PrintReservationStatus.Captured =>
                TerminalCommandId.HasValue &&
                DebitOperationId.HasValue,
            PrintReservationStatus.Released =>
                TerminalCommandId.HasValue &&
                DebitOperationId is null,
            _ => false
        };

        if (!validShape)
        {
            throw new InvalidDataException(
                $"Print reservation '{Id}' has an invalid state shape.");
        }
    }

    private static void ValidateOptionalId(
        Guid? value,
        string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new InvalidDataException(
                $"Print reservation value '{parameterName}' must not be empty.");
        }
    }

    private static void ValidateId(
        Guid value,
        string parameterName,
        string message)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(message, parameterName);
        }
    }
}
