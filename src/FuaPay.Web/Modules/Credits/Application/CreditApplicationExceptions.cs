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
