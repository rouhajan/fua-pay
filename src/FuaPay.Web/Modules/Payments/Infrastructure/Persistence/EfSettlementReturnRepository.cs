using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class EfSettlementReturnRepository :
    ISettlementReturnRepository
{
    private readonly FuaPayDbContext _dbContext;
    private readonly Dictionary<Guid, long> _loadedVersions = [];

    public EfSettlementReturnRepository(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<SettlementReturn?> FindByIdAsync(
        Guid settlementReturnId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(settlementReturnId, nameof(settlementReturnId));

        var entity = await _dbContext.SettlementReturns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == settlementReturnId,
                cancellationToken);

        return RestoreAndRemember(entity);
    }

    public async Task<SettlementReturn?> FindByRequestIdAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(requestId, nameof(requestId));

        var entity = await _dbContext.SettlementReturns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.RequestId == requestId,
                cancellationToken);

        return RestoreAndRemember(entity);
    }

    public async Task<SettlementReturn?> FindByOriginalPaymentIdAsync(
        Guid originalPaymentId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(originalPaymentId, nameof(originalPaymentId));

        var entity = await _dbContext.SettlementReturns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.OriginalPaymentId == originalPaymentId,
                cancellationToken);

        return RestoreAndRemember(entity);
    }

    public async Task<SettlementReturn?> FindByJobIdAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(jobId, nameof(jobId));

        var entity = await _dbContext.SettlementReturns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.JobId == jobId,
                cancellationToken);

        return RestoreAndRemember(entity);
    }

    public async Task AddAsync(
        SettlementReturn settlementReturn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settlementReturn);

        var entity = ToEntity(settlementReturn, version: 1);
        _dbContext.SettlementReturns.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                SettlementReturnConfiguration.RequestUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new SettlementReturnRequestAlreadyExistsException(
                settlementReturn.RequestId,
                exception);
        }
        catch (DbUpdateException exception)
            when (
                settlementReturn.OriginalPaymentId.HasValue &&
                IsUniqueViolation(
                    exception,
                    SettlementReturnConfiguration
                        .OriginalPaymentUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new SettlementReturnOriginalPaymentAlreadyExistsException(
                settlementReturn.OriginalPaymentId.Value,
                exception);
        }
        catch (DbUpdateException exception)
            when (
                settlementReturn.JobId.HasValue &&
                IsUniqueViolation(
                    exception,
                    SettlementReturnConfiguration.JobUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new SettlementReturnJobAlreadyExistsException(
                settlementReturn.JobId.Value,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[settlementReturn.Id] = entity.Version;
    }

    public async Task SaveAsync(
        SettlementReturn settlementReturn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settlementReturn);

        if (!_loadedVersions.TryGetValue(
                settlementReturn.Id,
                out var loadedVersion))
        {
            throw new InvalidOperationException(
                "Settlement return must be loaded by this repository " +
                "before it can be saved.");
        }

        var nextVersion = checked(loadedVersion + 1);
        var entity = ToEntity(settlementReturn, nextVersion);
        _dbContext.Attach(entity);

        var entry = _dbContext.Entry(entity);
        entry.State = EntityState.Modified;
        entry.Property(item => item.Version).OriginalValue = loadedVersion;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _dbContext.ChangeTracker.Clear();
            throw new SettlementReturnConcurrencyException(
                settlementReturn.Id,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(exception))
        {
            _dbContext.ChangeTracker.Clear();
            throw new SettlementReturnConcurrencyException(
                settlementReturn.Id,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[settlementReturn.Id] = nextVersion;
    }

    private SettlementReturn? RestoreAndRemember(
        SettlementReturnEntity? entity)
    {
        if (entity is null)
        {
            return null;
        }

        if (entity.Version <= 0)
        {
            throw new InvalidDataException(
                $"Settlement return '{entity.Id}' has invalid version.");
        }

        var settlementReturn = SettlementReturn.Restore(
            entity.Id,
            entity.RequestId,
            (SettlementReturnKind)entity.Kind,
            entity.OriginalPaymentId,
            entity.JobId,
            entity.CustomerUserId,
            entity.AdministratorUserId,
            new Money(entity.AmountMinorUnits),
            entity.Currency,
            entity.Reason,
            (SettlementReturnState)entity.State,
            entity.RequestedAt,
            entity.StartedAt,
            entity.UpdatedAt,
            entity.CompletedAt);

        _loadedVersions[settlementReturn.Id] = entity.Version;
        return settlementReturn;
    }

    private static SettlementReturnEntity ToEntity(
        SettlementReturn settlementReturn,
        long version)
    {
        return new SettlementReturnEntity
        {
            Id = settlementReturn.Id,
            RequestId = settlementReturn.RequestId,
            Kind = (int)settlementReturn.Kind,
            OriginalPaymentId = settlementReturn.OriginalPaymentId,
            JobId = settlementReturn.JobId,
            CustomerUserId = settlementReturn.CustomerUserId,
            AdministratorUserId = settlementReturn.AdministratorUserId,
            AmountMinorUnits = settlementReturn.Amount.MinorUnits,
            Currency = settlementReturn.Currency,
            Reason = settlementReturn.Reason,
            State = (int)settlementReturn.State,
            RequestedAt = settlementReturn.RequestedAt,
            StartedAt = settlementReturn.StartedAt,
            UpdatedAt = settlementReturn.UpdatedAt,
            CompletedAt = settlementReturn.CompletedAt,
            Version = version
        };
    }

    private static bool IsUniqueViolation(
        DbUpdateException exception,
        string? constraintName = null)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } postgresException &&
        (constraintName is null ||
         postgresException.ConstraintName == constraintName);
    }

    private static void ValidateId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Settlement return lookup ID must not be empty.",
                parameterName);
        }
    }
}
