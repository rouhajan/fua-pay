using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class EfManualCreditTopUpCommandRepository :
    IManualCreditTopUpCommandRepository
{
    private readonly FuaPayDbContext _dbContext;

    public EfManualCreditTopUpCommandRepository(
        FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<PersistedManualCreditTopUpCommand?> FindAsync(
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
                from commandEntity in _dbContext.ManualCreditTopUpCommands.AsNoTracking()
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
                $"Příkaz ručního dobití '{commandId}' neodpovídá vlastníku ledger pohybu.");
        }

        if (
            row.Movement.MovementType != (int)CreditMovementType.Credit ||
            row.Movement.AmountMinorUnits != row.Command.AmountMinorUnits)
        {
            throw new InvalidDataException(
                $"Příkaz ručního dobití '{commandId}' neodpovídá kreditnímu ledger pohybu.");
        }

        var command = new ManualCreditTopUpCommand(
            row.Command.CommandId,
            row.Command.AdministratorUserId,
            row.Command.OwnerId,
            new Money(row.Command.AmountMinorUnits),
            row.Command.Note);

        var result = new ManualCreditTopUpResult(
            commandId,
            (CreditMovementType)row.Movement.MovementType,
            new Money(row.Movement.AmountMinorUnits),
            new Money(row.Movement.BalanceAfterMinorUnits),
            row.Movement.RecordedAt,
            row.Movement.Description);

        return new PersistedManualCreditTopUpCommand(
            command,
            result,
            row.Command.AcceptedAt);
    }

    public void Stage(
        ManualCreditTopUpCommand command,
        DateTimeOffset acceptedAt)
    {
        ArgumentNullException.ThrowIfNull(command);

        _dbContext.ManualCreditTopUpCommands.Add(
            new ManualCreditTopUpCommandEntity
            {
                CommandId = command.CommandId,
                AdministratorUserId = command.AdministratorUserId,
                OwnerId = command.OwnerId,
                AmountMinorUnits = command.Amount.MinorUnits,
                Note = command.Note,
                AcceptedAt = acceptedAt
            });
    }
}
