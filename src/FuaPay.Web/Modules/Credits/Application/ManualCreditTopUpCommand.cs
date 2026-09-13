using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed record ManualCreditTopUpCommand
{
    public const int NoteMaxLength = 300;

    public ManualCreditTopUpCommand(
        Guid commandId,
        Guid administratorUserId,
        Guid ownerId,
        Money amount,
        string note)
    {
        ValidateId(commandId, nameof(commandId));
        ValidateId(administratorUserId, nameof(administratorUserId));
        ValidateId(ownerId, nameof(ownerId));

        if (!FinancialAmountPolicy.ManualCreditTopUp.Contains(amount))
        {
            throw new ManualCreditTopUpAmountNotAllowedException();
        }

        if (string.IsNullOrWhiteSpace(note))
        {
            throw new ManualCreditTopUpNoteNotAllowedException();
        }

        var normalizedNote = note.Trim();

        if (normalizedNote.Length > NoteMaxLength)
        {
            throw new ManualCreditTopUpNoteNotAllowedException();
        }

        CommandId = commandId;
        AdministratorUserId = administratorUserId;
        OwnerId = ownerId;
        Amount = amount;
        Note = normalizedNote;
    }

    public Guid CommandId { get; }

    public Guid AdministratorUserId { get; }

    public Guid OwnerId { get; }

    public Money Amount { get; }

    public string Note { get; }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "ID finančního příkazu ani jeho účastníka nesmí být prázdné.",
                parameterName);
        }
    }
}

public sealed record ManualCreditTopUpResult(
    Guid CommandId,
    CreditMovementType MovementType,
    Money Amount,
    Money BalanceAfter,
    DateTimeOffset RecordedAt,
    string Description);

public sealed record PersistedManualCreditTopUpCommand(
    ManualCreditTopUpCommand Command,
    ManualCreditTopUpResult Result,
    DateTimeOffset AcceptedAt);
