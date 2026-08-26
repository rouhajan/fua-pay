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

    public PrintReservationStatus Status { get; }

    public Guid ReserveCommandId { get; }

    public Guid? ResolutionCommandId { get; }

    public Guid? TerminalCommandId { get; }

    public Guid? DebitOperationId { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset StateChangedAt { get; }

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
