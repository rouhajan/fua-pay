using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class EfCreditQueries : ICreditQueries
{
    private readonly FuaPayDbContext _dbContext;

    public EfCreditQueries(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<CreditAdministrationMovementPage>
        ListAdministrationMovementsAsync(
            CreditAdministrationMovementFilter filter,
            CreditMovementPageRequest page,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var query =
            from movement in _dbContext.CreditMovements.AsNoTracking()
            join account in _dbContext.CreditAccounts.AsNoTracking()
                on movement.AccountId equals account.Id
            select new
            {
                account.OwnerId,
                Movement = movement
            };

        if (filter.OwnerId.HasValue)
        {
            query = query.Where(
                item => item.OwnerId == filter.OwnerId.Value);
        }

        if (filter.RecordedFrom.HasValue)
        {
            query = query.Where(
                item =>
                    item.Movement.RecordedAt >=
                    filter.RecordedFrom.Value);
        }

        if (filter.RecordedToExclusive.HasValue)
        {
            query = query.Where(
                item =>
                    item.Movement.RecordedAt <
                    filter.RecordedToExclusive.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(item => item.Movement.RecordedAt)
            .ThenByDescending(item => item.Movement.Id)
            .Skip(page.Offset)
            .Take(page.Limit)
            .Select(item => new CreditAdministrationMovementListItem(
                item.OwnerId,
                item.Movement.OperationId,
                (CreditMovementType)item.Movement.MovementType,
                item.Movement.AmountMinorUnits,
                item.Movement.BalanceAfterMinorUnits,
                item.Movement.Description,
                item.Movement.RecordedAt,
                item.Movement.Sequence))
            .ToArrayAsync(cancellationToken);

        return new CreditAdministrationMovementPage(
            items,
            page.Offset,
            page.Limit,
            totalCount);
    }

    public Task<CreditAccountSummary?> FindAccountForOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        ValidateOwnerId(ownerId);

        return _dbContext.CreditAccounts
            .AsNoTracking()
            .Where(account => account.OwnerId == ownerId)
            .Select(
                account => new CreditAccountSummary(
                    account.Id,
                    account.OwnerId,
                    account.BalanceMinorUnits,
                    account.Version))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<CreditMovementListItem?> FindMovementForOwnerAsync(
        Guid ownerId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        ValidateOwnerId(ownerId);

        if (operationId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID kreditní operace nesmí být prázdné.",
                nameof(operationId));
        }

        return (
            from movement in _dbContext.CreditMovements.AsNoTracking()
            join account in _dbContext.CreditAccounts.AsNoTracking()
                on movement.AccountId equals account.Id
            where
                account.OwnerId == ownerId &&
                movement.OperationId == operationId
            select new CreditMovementListItem(
                movement.OperationId,
                (CreditMovementType)movement.MovementType,
                movement.AmountMinorUnits,
                movement.BalanceAfterMinorUnits,
                movement.Description,
                movement.RecordedAt,
                movement.Sequence))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CreditMovementPage> ListMovementsForOwnerAsync(
        Guid ownerId,
        CreditMovementPageRequest page,
        CancellationToken cancellationToken = default)
    {
        ValidateOwnerId(ownerId);
        ArgumentNullException.ThrowIfNull(page);

        var query =
            from movement in _dbContext.CreditMovements.AsNoTracking()
            join account in _dbContext.CreditAccounts.AsNoTracking()
                on movement.AccountId equals account.Id
            where account.OwnerId == ownerId
            select movement;

        var totalCount =
            await query.LongCountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(movement => movement.Sequence)
            .Skip(page.Offset)
            .Take(page.Limit)
            .Select(
                movement => new CreditMovementListItem(
                    movement.OperationId,
                    (CreditMovementType)movement.MovementType,
                    movement.AmountMinorUnits,
                    movement.BalanceAfterMinorUnits,
                    movement.Description,
                    movement.RecordedAt,
                    movement.Sequence))
            .ToListAsync(cancellationToken);

        return new CreditMovementPage(
            items,
            page.Offset,
            page.Limit,
            totalCount);
    }

    private static void ValidateOwnerId(Guid ownerId)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID vlastníka kreditního účtu nesmí být prázdné.",
                nameof(ownerId));
        }
    }
}
