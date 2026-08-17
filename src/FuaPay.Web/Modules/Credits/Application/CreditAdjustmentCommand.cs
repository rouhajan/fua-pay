using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed record CreditAdjustmentCommand
{
    public const int ReasonMaxLength = 300;

    public CreditAdjustmentCommand(
        Guid commandId,
        Guid administratorUserId,
        Guid ownerId,
        Money signedAmount,
        string reason)
    {
        ValidateId(commandId, nameof(commandId));
        ValidateId(administratorUserId, nameof(administratorUserId));
        ValidateId(ownerId, nameof(ownerId));

        var range = FinancialAmountPolicy.CreditAdjustmentAbsolute;

        if (
            signedAmount.MinorUnits == 0 ||
            signedAmount.MinorUnits < -range.MaximumMinorUnits ||
            signedAmount.MinorUnits > range.MaximumMinorUnits)
        {
            throw new CreditAdjustmentAmountNotAllowedException();
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new CreditAdjustmentReasonNotAllowedException();
        }

        var normalizedReason = reason.Trim();

        if (normalizedReason.Length > ReasonMaxLength)
        {
            throw new CreditAdjustmentReasonNotAllowedException();
        }

        CommandId = commandId;
        AdministratorUserId = administratorUserId;
        OwnerId = ownerId;
        SignedAmount = signedAmount;
        Reason = normalizedReason;
    }

    public Guid CommandId { get; }

    public Guid AdministratorUserId { get; }

    public Guid OwnerId { get; }

    public Money SignedAmount { get; }

    public string Reason { get; }

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

public sealed record CreditAdjustmentResult(
    Guid CommandId,
    CreditMovementType MovementType,
    Money Amount,
    Money BalanceAfter,
    DateTimeOffset RecordedAt,
    string Description);

public sealed record PersistedCreditAdjustmentCommand(
    CreditAdjustmentCommand Command,
    CreditAdjustmentResult Result,
    DateTimeOffset AcceptedAt);
