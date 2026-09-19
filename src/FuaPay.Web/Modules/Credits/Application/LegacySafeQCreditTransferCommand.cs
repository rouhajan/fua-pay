using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed record LegacySafeQCreditTransferCommand
{
    public const int SafeQUserIdMaxLength = 64;
    public const int SnapshotSha256Length = 64;

    public LegacySafeQCreditTransferCommand(
        Guid commandId,
        Guid administratorUserId,
        Guid ownerId,
        string safeQUserId,
        string snapshotSha256,
        Money amount)
    {
        ValidateId(commandId, nameof(commandId));
        ValidateId(administratorUserId, nameof(administratorUserId));
        ValidateId(ownerId, nameof(ownerId));

        if (string.IsNullOrWhiteSpace(safeQUserId))
        {
            throw new LegacySafeQUserIdNotAllowedException();
        }

        var normalizedSafeQUserId = safeQUserId.Trim();

        if (normalizedSafeQUserId.Length > SafeQUserIdMaxLength)
        {
            throw new LegacySafeQUserIdNotAllowedException();
        }

        if (string.IsNullOrWhiteSpace(snapshotSha256))
        {
            throw new LegacySafeQSnapshotHashNotAllowedException();
        }

        var normalizedSnapshotSha256 =
            snapshotSha256.Trim().ToUpperInvariant();

        if (
            normalizedSnapshotSha256.Length != SnapshotSha256Length ||
            normalizedSnapshotSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new LegacySafeQSnapshotHashNotAllowedException();
        }

        var maximum =
            FinancialAmountPolicy.CreditAdjustmentAbsolute.MaximumMinorUnits;

        if (
            amount.MinorUnits <= 0 ||
            amount.MinorUnits > maximum)
        {
            throw new LegacySafeQCreditTransferAmountNotAllowedException();
        }

        CommandId = commandId;
        AdministratorUserId = administratorUserId;
        OwnerId = ownerId;
        SafeQUserId = normalizedSafeQUserId;
        SnapshotSha256 = normalizedSnapshotSha256;
        Amount = amount;
    }

    public Guid CommandId { get; }

    public Guid AdministratorUserId { get; }

    public Guid OwnerId { get; }

    public string SafeQUserId { get; }

    public string SnapshotSha256 { get; }

    public Money Amount { get; }

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

public sealed record LegacySafeQCreditTransferResult(
    Guid CommandId,
    CreditMovementType MovementType,
    Money Amount,
    Money BalanceAfter,
    DateTimeOffset RecordedAt,
    string Description);

public sealed record PersistedLegacySafeQCreditTransfer(
    LegacySafeQCreditTransferCommand Command,
    LegacySafeQCreditTransferResult Result,
    DateTimeOffset AcceptedAt);
