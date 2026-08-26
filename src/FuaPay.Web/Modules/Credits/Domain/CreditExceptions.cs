namespace FuaPay.Web.Modules.Credits.Domain;

public sealed class InsufficientCreditException : InvalidOperationException
{
    public InsufficientCreditException()
        : base("Kreditní účet nemá dostatečný zůstatek.")
    {
    }
}

public sealed class DuplicateCreditOperationException : InvalidOperationException
{
    public DuplicateCreditOperationException(Guid operationId)
        : base($"Kreditní operace '{operationId}' již byla zpracována.")
    {
        OperationId = operationId;
    }

    public Guid OperationId { get; }
}

public sealed class InvalidPrintReservationStateTransitionException :
    InvalidOperationException
{
    public InvalidPrintReservationStateTransitionException(
        PrintReservationStatus currentStatus,
        PrintReservationStatus targetStatus)
        : base(
            $"Print reservation cannot transition from '{currentStatus}' " +
            $"to '{targetStatus}'.")
    {
        CurrentStatus = currentStatus;
        TargetStatus = targetStatus;
    }

    public PrintReservationStatus CurrentStatus { get; }

    public PrintReservationStatus TargetStatus { get; }
}
