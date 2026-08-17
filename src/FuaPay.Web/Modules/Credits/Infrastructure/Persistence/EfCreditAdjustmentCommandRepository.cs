using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class EfCreditAdjustmentCommandRepository :
    ICreditAdjustmentCommandRepository
{
    private readonly FuaPayDbContext _dbContext;

    public EfCreditAdjustmentCommandRepository(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<PersistedCreditAdjustmentCommand?> FindAsync(
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
                from commandEntity in _dbContext.CreditAdjustmentCommands.AsNoTracking()
                join movement in _dbContext.CreditMovements.AsNoTracking()
                    on commandEntity.CommandId equals movement.OperationId
                join account in _dbContext.CreditAccounts.AsNoTracking()
                    on movement.AccountId equals account.Id
                where commandEntity.CommandId == commandId
                select new
                {
                    Command = commandEntity,
                    Movement = movement,
                    AccountOwnerId = account.OwnerId
                })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        if (row.AccountOwnerId != row.Command.OwnerId)
        {
            throw new InvalidDataException(
                $"Příkaz korekce '{commandId}' neodpovídá vlastníku ledger pohybu.");
        }

        var command = new CreditAdjustmentCommand(
            row.Command.CommandId,
            row.Command.AdministratorUserId,
            row.Command.OwnerId,
            new Money(row.Command.SignedAmountMinorUnits),
            row.Command.Reason);

        var result = new CreditAdjustmentResult(
            commandId,
            (CreditMovementType)row.Movement.MovementType,
            new Money(row.Movement.AmountMinorUnits),
            new Money(row.Movement.BalanceAfterMinorUnits),
            row.Movement.RecordedAt,
            row.Movement.Description);

        return new PersistedCreditAdjustmentCommand(
            command,
            result,
            row.Command.AcceptedAt);
    }

    public void Stage(
        CreditAdjustmentCommand command,
        DateTimeOffset acceptedAt)
    {
        ArgumentNullException.ThrowIfNull(command);

        _dbContext.CreditAdjustmentCommands.Add(
            new CreditAdjustmentCommandEntity
            {
                CommandId = command.CommandId,
                AdministratorUserId = command.AdministratorUserId,
                OwnerId = command.OwnerId,
                SignedAmountMinorUnits = command.SignedAmount.MinorUnits,
                Reason = command.Reason,
                AcceptedAt = acceptedAt
            });
    }
}
