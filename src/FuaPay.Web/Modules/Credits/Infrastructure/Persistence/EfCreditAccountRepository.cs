using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class EfCreditAccountRepository :
    ICreditAccountRepository
{
    private readonly FuaPayDbContext _dbContext;

    private readonly Dictionary<Guid, LoadedAccountState>
        _loadedAccounts = [];

    public EfCreditAccountRepository(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<CreditAccount?> FindByOwnerIdAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.CreditAccounts
            .AsNoTracking()
            .Include(account => account.Movements)
            .SingleOrDefaultAsync(
                account => account.OwnerId == ownerId,
                cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var account = Restore(entity);

        _loadedAccounts[account.Id] = new LoadedAccountState(
            entity.Version,
            entity.Movements
                .Select(movement => movement.OperationId)
                .ToHashSet(),
            entity.Movements.Count == 0
                ? 0
                : entity.Movements.Max(
                    movement => movement.Sequence));

        return account;
    }

    public async Task<CreditAccount?> FindByOwnerIdForUpdateAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        ValidateOwnerId(ownerId);
        EnsureActiveTransaction();

        var lockedAccounts = await _dbContext.CreditAccounts
            .FromSqlInterpolated(
                $"SELECT * FROM credits.accounts WHERE owner_id = {ownerId} FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (lockedAccounts.Count > 1)
        {
            throw new InvalidDataException(
                $"Owner '{ownerId}' has multiple credit accounts.");
        }

        return lockedAccounts.Count == 0
            ? null
            : await FindByOwnerIdAsync(
                ownerId,
                cancellationToken);
    }

    public async Task LockOwnerForAccountCreationAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        ValidateOwnerId(ownerId);
        EnsureActiveTransaction();

        _ = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended('fua-pay:credits:account:' || CAST({ownerId} AS text), 0))",
            cancellationToken);
    }

    public async Task AddAsync(
        CreditAccount account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        var entity = CreateNewEntity(account);

        var operationId =
            account.Movements.Count == 1
                ? account.Movements[0].OperationId
                : (Guid?)null;

        _dbContext.CreditAccounts.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                CreditAdjustmentCommandConfiguration
                    .PrimaryKeyConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw new CreditAdjustmentCommandAlreadyExistsException(
                operationId ?? Guid.Empty,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                CreditAccountConfiguration.OwnerUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw new CreditAccountConcurrencyException(
                account.OwnerId,
                exception);
        }
        catch (DbUpdateException exception)
            when (
                operationId.HasValue &&
                IsUniqueViolation(
                    exception,
                    CreditMovementConfiguration
                        .OperationUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw new DuplicateCreditOperationException(
                operationId.Value);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();

        _loadedAccounts[account.Id] = new LoadedAccountState(
            entity.Version,
            account.Movements
                .Select(movement => movement.OperationId)
                .ToHashSet(),
            account.Movements.Count);
    }

    public async Task SaveAsync(
        CreditAccount account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (!_loadedAccounts.TryGetValue(
            account.Id,
            out var loadedState))
        {
            throw new InvalidOperationException(
                "Kreditní účet musí být před uložením načten " +
                "stejnou instancí repozitáře.");
        }

        var nextVersion = checked(loadedState.Version + 1);

        var accountEntity = new CreditAccountEntity
        {
            Id = account.Id,
            OwnerId = account.OwnerId,
            BalanceMinorUnits = account.Balance.MinorUnits,
            Version = nextVersion
        };

        _dbContext.Attach(accountEntity);

        var accountEntry = _dbContext.Entry(accountEntity);

        accountEntry
            .Property(entity => entity.BalanceMinorUnits)
            .IsModified = true;

        accountEntry
            .Property(entity => entity.Version)
            .OriginalValue = loadedState.Version;

        accountEntry
            .Property(entity => entity.Version)
            .IsModified = true;

        var sequence = loadedState.LastSequence;

        var newMovements = account.Movements
            .Where(
                movement =>
                    !loadedState.OperationIds.Contains(
                        movement.OperationId))
            .ToArray();

        foreach (var movement in newMovements)
        {
            sequence = checked(sequence + 1);

            _dbContext.CreditMovements.Add(
                CreateMovementEntity(
                    account.Id,
                    sequence,
                    movement));
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _dbContext.ChangeTracker.Clear();

            throw new CreditAccountConcurrencyException(
                account.OwnerId,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                CreditAdjustmentCommandConfiguration
                    .PrimaryKeyConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw new CreditAdjustmentCommandAlreadyExistsException(
                newMovements.Length == 1
                    ? newMovements[0].OperationId
                    : Guid.Empty,
                exception);
        }
        catch (DbUpdateException exception)
            when (
                newMovements.Length == 1 &&
                IsUniqueViolation(
                    exception,
                    CreditMovementConfiguration
                        .OperationUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw new DuplicateCreditOperationException(
                newMovements[0].OperationId);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                CreditMovementConfiguration
                    .AccountSequenceUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw new CreditAccountConcurrencyException(
                account.OwnerId,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();

        _loadedAccounts[account.Id] = new LoadedAccountState(
            nextVersion,
            account.Movements
                .Select(movement => movement.OperationId)
                .ToHashSet(),
            sequence);
    }

    private static CreditAccount Restore(
        CreditAccountEntity entity)
    {
        var account = new CreditAccount(
            entity.Id,
            entity.OwnerId);

        var expectedSequence = 1L;

        foreach (var movementEntity in
            entity.Movements.OrderBy(
                movement => movement.Sequence))
        {
            if (movementEntity.Sequence != expectedSequence)
            {
                throw new InvalidDataException(
                    $"Kreditní účet '{entity.Id}' obsahuje " +
                    "neplatné pořadí pohybů.");
            }

            var amount = new Money(
                movementEntity.AmountMinorUnits);

            CreditMovement movement =
                movementEntity.MovementType switch
                {
                    (int)CreditMovementType.Credit =>
                        account.Credit(
                            movementEntity.OperationId,
                            amount,
                            movementEntity.RecordedAt,
                            movementEntity.Description),

                    (int)CreditMovementType.Debit =>
                        account.Debit(
                            movementEntity.OperationId,
                            amount,
                            movementEntity.RecordedAt,
                            movementEntity.Description),

                    _ => throw new InvalidDataException(
                        $"Kreditní účet '{entity.Id}' obsahuje " +
                        "neplatný typ pohybu.")
                };

            if (
                movement.BalanceAfter.MinorUnits !=
                movementEntity.BalanceAfterMinorUnits)
            {
                throw new InvalidDataException(
                    $"Kreditní účet '{entity.Id}' obsahuje " +
                    "nekonzistentní zůstatek pohybu.");
            }

            expectedSequence++;
        }

        if (
            account.Balance.MinorUnits !=
            entity.BalanceMinorUnits)
        {
            throw new InvalidDataException(
                $"Kreditní účet '{entity.Id}' obsahuje " +
                "nekonzistentní konečný zůstatek.");
        }

        return account;
    }

    private static CreditAccountEntity CreateNewEntity(
        CreditAccount account)
    {
        var entity = new CreditAccountEntity
        {
            Id = account.Id,
            OwnerId = account.OwnerId,
            BalanceMinorUnits = account.Balance.MinorUnits,
            Version = 1
        };

        long sequence = 0;

        foreach (var movement in account.Movements)
        {
            sequence++;

            entity.Movements.Add(
                CreateMovementEntity(
                    account.Id,
                    sequence,
                    movement));
        }

        return entity;
    }

    private static CreditMovementEntity CreateMovementEntity(
        Guid accountId,
        long sequence,
        CreditMovement movement)
    {
        return new CreditMovementEntity
        {
            AccountId = accountId,
            Sequence = sequence,
            OperationId = movement.OperationId,
            MovementType = (int)movement.Type,
            AmountMinorUnits = movement.Amount.MinorUnits,
            BalanceAfterMinorUnits =
                movement.BalanceAfter.MinorUnits,
            RecordedAt = movement.RecordedAt,
            Description = movement.Description
        };
    }

    private static bool IsUniqueViolation(
        DbUpdateException exception,
        string constraintName)
    {
        return
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            } postgresException &&
            postgresException.ConstraintName == constraintName;
    }

    private void EnsureActiveTransaction()
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A credit account lock requires an active database transaction.");
        }
    }

    private static void ValidateOwnerId(Guid ownerId)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException(
                "The credit account owner ID must not be empty.",
                nameof(ownerId));
        }
    }

    private sealed record LoadedAccountState(
        long Version,
        HashSet<Guid> OperationIds,
        long LastSequence);
}
