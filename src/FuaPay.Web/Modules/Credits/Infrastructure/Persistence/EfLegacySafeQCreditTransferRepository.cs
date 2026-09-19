using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class EfLegacySafeQCreditTransferRepository :
    ILegacySafeQCreditTransferRepository
{
    private readonly FuaPayDbContext _dbContext;

    public EfLegacySafeQCreditTransferRepository(
        FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<PersistedLegacySafeQCreditTransfer?>
        FindByCommandIdAsync(
            Guid commandId,
            CancellationToken cancellationToken = default)
    {
        if (commandId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID příkazu nesmí být prázdné.",
                nameof(commandId));
        }

        var row = await (
                from transfer in
                    _dbContext.LegacySafeQCreditTransfers.AsNoTracking()
                join movement in _dbContext.CreditMovements.AsNoTracking()
                    on transfer.CommandId equals movement.OperationId
                join account in _dbContext.CreditAccounts.AsNoTracking()
                    on movement.AccountId equals account.Id
                where transfer.CommandId == commandId
                select new
                {
                    Transfer = transfer,
                    Movement = movement,
                    AccountOwnerId = account.OwnerId
                })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : Restore(
                row.Transfer,
                row.Movement,
                row.AccountOwnerId);
    }

    public async Task<PersistedLegacySafeQCreditTransfer?>
        FindBySafeQUserIdAsync(
            string safeQUserId,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(safeQUserId))
        {
            throw new ArgumentException(
                "SafeQ user ID nesmí být prázdné.",
                nameof(safeQUserId));
        }

        var normalizedSafeQUserId = safeQUserId.Trim();

        var row = await (
                from transfer in
                    _dbContext.LegacySafeQCreditTransfers.AsNoTracking()
                join movement in _dbContext.CreditMovements.AsNoTracking()
                    on transfer.CommandId equals movement.OperationId
                join account in _dbContext.CreditAccounts.AsNoTracking()
                    on movement.AccountId equals account.Id
                where transfer.SafeQUserId == normalizedSafeQUserId
                select new
                {
                    Transfer = transfer,
                    Movement = movement,
                    AccountOwnerId = account.OwnerId
                })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : Restore(
                row.Transfer,
                row.Movement,
                row.AccountOwnerId);
    }

    public void Stage(
        LegacySafeQCreditTransferCommand command,
        DateTimeOffset acceptedAt)
    {
        ArgumentNullException.ThrowIfNull(command);

        _dbContext.LegacySafeQCreditTransfers.Add(
            new LegacySafeQCreditTransferEntity
            {
                CommandId = command.CommandId,
                AdministratorUserId = command.AdministratorUserId,
                OwnerId = command.OwnerId,
                SafeQUserId = command.SafeQUserId,
                SnapshotSha256 = command.SnapshotSha256,
                AmountMinorUnits = command.Amount.MinorUnits,
                AcceptedAt = acceptedAt
            });
    }

    private static PersistedLegacySafeQCreditTransfer Restore(
        LegacySafeQCreditTransferEntity transfer,
        CreditMovementEntity movement,
        Guid accountOwnerId)
    {
        if (accountOwnerId != transfer.OwnerId)
        {
            throw new InvalidDataException(
                $"SafeQ transfer '{transfer.CommandId}' neodpovídá " +
                "vlastníku ledger pohybu.");
        }

        if (
            movement.MovementType != (int)CreditMovementType.Credit ||
            movement.AmountMinorUnits != transfer.AmountMinorUnits)
        {
            throw new InvalidDataException(
                $"SafeQ transfer '{transfer.CommandId}' neodpovídá " +
                "kreditnímu ledger pohybu.");
        }

        var command = new LegacySafeQCreditTransferCommand(
            transfer.CommandId,
            transfer.AdministratorUserId,
            transfer.OwnerId,
            transfer.SafeQUserId,
            transfer.SnapshotSha256,
            new Money(transfer.AmountMinorUnits));

        var result = new LegacySafeQCreditTransferResult(
            transfer.CommandId,
            (CreditMovementType)movement.MovementType,
            new Money(movement.AmountMinorUnits),
            new Money(movement.BalanceAfterMinorUnits),
            movement.RecordedAt,
            movement.Description);

        return new PersistedLegacySafeQCreditTransfer(
            command,
            result,
            transfer.AcceptedAt);
    }
}
