using System.Collections.ObjectModel;

using FuaPay.Web.BuildingBlocks.Domain;

namespace FuaPay.Web.Modules.Credits.Domain;

public sealed class CreditAccount
{
    private readonly List<CreditMovement> _movements = [];
    private readonly ReadOnlyCollection<CreditMovement> _readOnlyMovements;

    public CreditAccount(Guid id, Guid ownerId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "ID kreditního účtu nesmí být prázdné.",
                nameof(id));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID vlastníka nesmí být prázdné.",
                nameof(ownerId));
        }

        Id = id;
        OwnerId = ownerId;
        Balance = Money.Zero;
        _readOnlyMovements = _movements.AsReadOnly();
    }

    public Guid Id { get; }

    public Guid OwnerId { get; }

    public Money Balance { get; private set; }

    public IReadOnlyList<CreditMovement> Movements => _readOnlyMovements;

    public CreditMovement Credit(
        Guid operationId,
        Money amount,
        DateTimeOffset recordedAt,
        string description)
    {
        ValidateOperation(operationId, amount, description);

        var newBalance = Balance.Add(amount);

        var movement = new CreditMovement(
            operationId,
            CreditMovementType.Credit,
            amount,
            newBalance,
            recordedAt,
            description.Trim());

        _movements.Add(movement);
        Balance = newBalance;

        return movement;
    }

    public CreditMovement Debit(
        Guid operationId,
        Money amount,
        DateTimeOffset recordedAt,
        string description)
    {
        return DebitCore(
            operationId,
            amount,
            Balance,
            recordedAt,
            description);
    }

    internal CreditMovement Debit(
        Guid operationId,
        Money amount,
        Money spendableBalance,
        DateTimeOffset recordedAt,
        string description)
    {
        return DebitCore(
            operationId,
            amount,
            spendableBalance,
            recordedAt,
            description);
    }

    private CreditMovement DebitCore(
        Guid operationId,
        Money amount,
        Money spendableBalance,
        DateTimeOffset recordedAt,
        string description)
    {
        ValidateOperation(operationId, amount, description);

        if (
            amount.MinorUnits > spendableBalance.MinorUnits ||
            amount.MinorUnits > Balance.MinorUnits)
        {
            throw new InsufficientCreditException();
        }

        var newBalance = Balance.Subtract(amount);

        var movement = new CreditMovement(
            operationId,
            CreditMovementType.Debit,
            amount,
            newBalance,
            recordedAt,
            description.Trim());

        _movements.Add(movement);
        Balance = newBalance;

        return movement;
    }

    private void ValidateOperation(
        Guid operationId,
        Money amount,
        string description)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID operace nesmí být prázdné.",
                nameof(operationId));
        }

        if (amount.MinorUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Částka operace musí být kladná.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Popis operace nesmí být prázdný.",
                nameof(description));
        }

        if (_movements.Any(
            movement => movement.OperationId == operationId))
        {
            throw new DuplicateCreditOperationException(operationId);
        }
    }
}
