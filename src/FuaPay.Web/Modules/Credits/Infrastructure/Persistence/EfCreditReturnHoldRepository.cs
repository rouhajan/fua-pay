using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class EfCreditReturnHoldRepository :
    ICreditReturnHoldRepository
{
    private readonly FuaPayDbContext _dbContext;
    private readonly Dictionary<Guid, long> _loadedVersions = [];

    public EfCreditReturnHoldRepository(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<CreditReturnHold?> FindBySettlementReturnIdAsync(
        Guid settlementReturnId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(settlementReturnId);

        var entity = await _dbContext.CreditReturnHolds
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.SettlementReturnId == settlementReturnId,
                cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<CreditReturnHold?>
        FindBySettlementReturnIdForUpdateAsync(
            Guid settlementReturnId,
            CancellationToken cancellationToken = default)
    {
        ValidateId(settlementReturnId);
        EnsureActiveTransaction();

        var entity = await _dbContext.CreditReturnHolds
            .FromSqlInterpolated(
                $"SELECT * FROM credits.return_holds WHERE settlement_return_id = {settlementReturnId} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            return null;
        }

        _loadedVersions[entity.SettlementReturnId] = entity.Version;
        return ToDomain(entity);
    }

    public async Task AddAsync(
        CreditReturnHold hold,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hold);

        var entity = ToEntity(hold, version: 1);
        _dbContext.CreditReturnHolds.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                CreditReturnHoldConfiguration.PrimaryKeyConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new CreditReturnHoldAlreadyExistsException(
                hold.SettlementReturnId,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[hold.SettlementReturnId] = 1;
    }

    public async Task SaveAsync(
        CreditReturnHold hold,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hold);
        EnsureActiveTransaction();

        if (!_loadedVersions.TryGetValue(
                hold.SettlementReturnId,
                out var loadedVersion))
        {
            throw new InvalidOperationException(
                "A credit return hold must be locked before it can be saved.");
        }

        var nextVersion = checked(loadedVersion + 1);
        var entity = ToEntity(hold, nextVersion);
        _dbContext.Attach(entity);
        var entry = _dbContext.Entry(entity);

        entry.Property(item => item.State).IsModified = true;
        entry.Property(item => item.StateChangedAt).IsModified = true;
        entry.Property(item => item.Version).OriginalValue = loadedVersion;
        entry.Property(item => item.Version).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _dbContext.ChangeTracker.Clear();
            throw new CreditReturnHoldConcurrencyException(
                hold.SettlementReturnId,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[hold.SettlementReturnId] = nextVersion;
    }

    private static CreditReturnHold ToDomain(
        CreditReturnHoldEntity entity)
    {
        if (entity.Version <= 0)
        {
            throw new InvalidDataException(
                $"Credit return hold '{entity.SettlementReturnId}' has an invalid version.");
        }

        return CreditReturnHold.Restore(
            entity.SettlementReturnId,
            entity.CreditAccountId,
            new Money(entity.AmountMinorUnits),
            (CreditReturnHoldState)entity.State,
            entity.CreatedAt,
            entity.StateChangedAt);
    }

    private static CreditReturnHoldEntity ToEntity(
        CreditReturnHold hold,
        long version)
    {
        return new CreditReturnHoldEntity
        {
            SettlementReturnId = hold.SettlementReturnId,
            CreditAccountId = hold.CreditAccountId,
            AmountMinorUnits = hold.Amount.MinorUnits,
            State = (int)hold.State,
            CreatedAt = hold.CreatedAt,
            StateChangedAt = hold.StateChangedAt,
            Version = version
        };
    }

    private void EnsureActiveTransaction()
    {
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A credit return hold lock or update requires an active database transaction.");
        }
    }

    private static bool IsUniqueViolation(
        DbUpdateException exception,
        string constraintName)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } postgresException &&
        postgresException.ConstraintName == constraintName;
    }

    private static void ValidateId(Guid settlementReturnId)
    {
        if (settlementReturnId == Guid.Empty)
        {
            throw new ArgumentException(
                "Settlement return ID must not be empty.",
                nameof(settlementReturnId));
        }
    }
}
