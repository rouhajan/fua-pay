using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class EfSettlementReturnProviderAttemptRepository :
    ISettlementReturnProviderAttemptRepository
{
    private readonly FuaPayDbContext _dbContext;
    private readonly Dictionary<Guid, long> _loadedVersions = [];

    public EfSettlementReturnProviderAttemptRepository(
        FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<SettlementReturnProviderAttempt?> FindByIdAsync(
        Guid attemptId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(attemptId, nameof(attemptId));

        var entity = await _dbContext.SettlementReturnProviderAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == attemptId,
                cancellationToken);

        return RestoreAndRemember(entity);
    }

    public async Task<SettlementReturnProviderAttempt?>
        FindActiveBySettlementReturnIdAsync(
            Guid settlementReturnId,
            CancellationToken cancellationToken = default)
    {
        ValidateId(settlementReturnId, nameof(settlementReturnId));

        var entity = await _dbContext.SettlementReturnProviderAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.SettlementReturnId == settlementReturnId &&
                    (item.State ==
                        (int)SettlementReturnProviderAttemptState.Prepared ||
                     item.State ==
                        (int)SettlementReturnProviderAttemptState.InProgress ||
                     item.State ==
                        (int)SettlementReturnProviderAttemptState.Uncertain),
                cancellationToken);

        return RestoreAndRemember(entity);
    }

    public async Task<IReadOnlyList<SettlementReturnProviderAttempt>>
        ListBySettlementReturnIdAsync(
            Guid settlementReturnId,
            CancellationToken cancellationToken = default)
    {
        ValidateId(settlementReturnId, nameof(settlementReturnId));

        var entities = await _dbContext.SettlementReturnProviderAttempts
            .AsNoTracking()
            .Where(item =>
                item.SettlementReturnId == settlementReturnId)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

        return entities
            .Select(entity => RestoreAndRemember(entity)!)
            .ToArray();
    }

    public async Task AddAsync(
        SettlementReturnProviderAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var entity = ToEntity(attempt, version: 1);
        _dbContext.SettlementReturnProviderAttempts.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                SettlementReturnProviderAttemptConfiguration
                    .PrimaryKeyConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new SettlementReturnProviderAttemptAlreadyExistsException(
                attempt.Id,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                SettlementReturnProviderAttemptConfiguration
                    .AttemptSequenceUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            var blocking = await _dbContext
                .SettlementReturnProviderAttempts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item =>
                        item.SettlementReturnId ==
                            attempt.SettlementReturnId &&
                        (item.State ==
                            (int)SettlementReturnProviderAttemptState.Prepared ||
                         item.State ==
                            (int)SettlementReturnProviderAttemptState.InProgress ||
                         item.State ==
                            (int)SettlementReturnProviderAttemptState.Confirmed ||
                         item.State ==
                            (int)SettlementReturnProviderAttemptState.Uncertain),
                    cancellationToken);

            if (blocking?.State ==
                (int)SettlementReturnProviderAttemptState.Confirmed)
            {
                throw new SettlementReturnProviderAttemptNotAllowedException(
                    attempt.SettlementReturnId,
                    "a provider attempt has already been confirmed");
            }

            throw new SettlementReturnProviderAttemptAlreadyActiveException(
                attempt.SettlementReturnId,
                blocking?.Id,
                innerException: exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[attempt.Id] = entity.Version;
    }

    public async Task SaveAsync(
        SettlementReturnProviderAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (!_loadedVersions.TryGetValue(
                attempt.Id,
                out var loadedVersion))
        {
            throw new InvalidOperationException(
                "Settlement return provider attempt must be loaded by " +
                "this repository before it can be saved.");
        }

        var nextVersion = checked(loadedVersion + 1);
        var entity = ToEntity(attempt, nextVersion);
        _dbContext.Attach(entity);
        var entry = _dbContext.Entry(entity);

        entry.Property(item => item.State).IsModified = true;
        entry.Property(item => item.Diagnostic).IsModified = true;
        entry.Property(item => item.UpdatedAt).IsModified = true;
        entry.Property(item => item.StartedAt).IsModified = true;
        entry.Property(item => item.FinishedAt).IsModified = true;
        entry.Property(item => item.Version).OriginalValue = loadedVersion;
        entry.Property(item => item.Version).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _dbContext.ChangeTracker.Clear();
            throw new SettlementReturnProviderAttemptConcurrencyException(
                attempt.Id,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[attempt.Id] = nextVersion;
    }

    private SettlementReturnProviderAttempt? RestoreAndRemember(
        SettlementReturnProviderAttemptEntity? entity)
    {
        if (entity is null)
        {
            return null;
        }

        if (entity.Version <= 0)
        {
            throw new InvalidDataException(
                $"Settlement return provider attempt '{entity.Id}' has " +
                "an invalid version.");
        }

        var attempt = SettlementReturnProviderAttempt.Restore(
            entity.Id,
            entity.SettlementReturnId,
            (PaymentProvider)entity.Provider,
            (SettlementReturnProviderOperation)entity.Operation,
            entity.ProviderReference,
            (SettlementReturnProviderAttemptState)entity.State,
            entity.Diagnostic,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.StartedAt,
            entity.FinishedAt);

        _loadedVersions[attempt.Id] = entity.Version;
        return attempt;
    }

    private static SettlementReturnProviderAttemptEntity ToEntity(
        SettlementReturnProviderAttempt attempt,
        long version)
    {
        return new SettlementReturnProviderAttemptEntity
        {
            Id = attempt.Id,
            SettlementReturnId = attempt.SettlementReturnId,
            Provider = (int)attempt.Provider,
            Operation = (int)attempt.Operation,
            ProviderReference = attempt.ProviderReference,
            State = (int)attempt.State,
            Diagnostic = attempt.Diagnostic,
            CreatedAt = attempt.CreatedAt,
            UpdatedAt = attempt.UpdatedAt,
            StartedAt = attempt.StartedAt,
            FinishedAt = attempt.FinishedAt,
            Version = version
        };
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

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Settlement return provider attempt lookup ID must not be " +
                "empty.",
                parameterName);
        }
    }
}
