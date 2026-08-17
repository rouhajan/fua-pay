using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace FuaPay.Web.Modules.Jobs.Infrastructure.Persistence;

internal sealed class EfJobRepository : IJobRepository
{
    private readonly FuaPayDbContext _dbContext;

    private readonly Dictionary<Guid, long> _loadedVersions = [];

    public EfJobRepository(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<Job?> FindByIdAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zakázky nesmí být prázdné.",
                nameof(jobId));
        }

        var entity =
            await _dbContext.Jobs
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    job => job.Id == jobId,
                    cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var job = Restore(entity);

        _loadedVersions[job.Id] = entity.Version;

        return job;
    }

    public async Task AddAsync(
        Job job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var entity =
            CreateEntity(
                job,
                version: 1);

        _dbContext.Jobs.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                JobConfiguration.PrimaryKeyConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw new JobConcurrencyException(
                job.Id,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                JobConfiguration.NumberUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw new JobNumberAlreadyUsedException(
                job.Number,
                exception);
        }
        catch (DbUpdateException exception)
            when (
                HasSettlement(job) &&
                IsUniqueViolation(
                    exception,
                    JobConfiguration
                        .SettlementReferenceUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw CreateSettlementReferenceException(
                job,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[job.Id] = entity.Version;
    }

    public async Task SaveAsync(
        Job job,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!_loadedVersions.TryGetValue(
            job.Id,
            out var loadedVersion))
        {
            throw new InvalidOperationException(
                "Zakázka musí být před uložením načtena " +
                "stejnou instancí repozitáře.");
        }

        var nextVersion =
            checked(loadedVersion + 1);

        var entity =
            CreateEntity(
                job,
                nextVersion);

        _dbContext.Attach(entity);

        var entry =
            _dbContext.Entry(entity);

        entry.Property(item => item.CustomerUserId)
            .IsModified = true;

        entry.Property(item => item.ServiceType)
            .IsModified = true;

        entry.Property(item => item.Title)
            .IsModified = true;

        entry.Property(item => item.Description)
            .IsModified = true;

        entry.Property(item => item.PriceMinorUnits)
            .IsModified = true;

        entry.Property(item => item.ProductionStatus)
            .IsModified = true;

        entry.Property(item => item.PaymentStatus)
            .IsModified = true;

        entry.Property(item => item.SettlementType)
            .IsModified = true;

        entry.Property(item => item.SettlementReferenceId)
            .IsModified = true;

        entry.Property(item => item.PublishedAt)
            .IsModified = true;

        entry.Property(item => item.SettledAt)
            .IsModified = true;

        entry.Property(item => item.ProductionStartedAt)
            .IsModified = true;

        entry.Property(item => item.ReadyForPickupAt)
            .IsModified = true;

        entry.Property(item => item.CompletedAt)
            .IsModified = true;

        entry.Property(item => item.CancelledAt)
            .IsModified = true;

        entry.Property(item => item.Version)
            .OriginalValue = loadedVersion;

        entry.Property(item => item.Version)
            .IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _dbContext.ChangeTracker.Clear();

            throw new JobConcurrencyException(
                job.Id,
                exception);
        }
        catch (DbUpdateException exception)
            when (
                HasSettlement(job) &&
                IsUniqueViolation(
                    exception,
                    JobConfiguration
                        .SettlementReferenceUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();

            throw CreateSettlementReferenceException(
                job,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[job.Id] = nextVersion;
    }

    private static Job Restore(JobEntity entity)
    {
        if (entity.Version <= 0)
        {
            throw new InvalidDataException(
                $"Zakázka '{entity.Id}' má neplatnou verzi.");
        }

        try
        {
            var productionStatus =
                (JobProductionStatus)entity.ProductionStatus;

            var paymentStatus =
                (JobPaymentStatus)entity.PaymentStatus;

            var job = new Job(
                entity.Id,
                entity.Number,
                entity.ServiceUnitId,
                entity.CustomerUserId,
                entity.CreatedByUserId,
                (ServiceType)entity.ServiceType,
                entity.Title,
                entity.Description,
                new Money(entity.PriceMinorUnits),
                entity.CreatedAt);

            switch (productionStatus)
            {
                case JobProductionStatus.Draft:
                    RestoreSettlement(
                        job,
                        entity,
                        paymentStatus);
                    break;

                case JobProductionStatus.Published:
                    job.Publish(
                        RequireTimestamp(
                            entity.PublishedAt,
                            entity.Id,
                            nameof(entity.PublishedAt)));

                    RestoreSettlement(
                        job,
                        entity,
                        paymentStatus);
                    break;

                case JobProductionStatus.InProduction:
                    RestorePublishedAndSettlement(
                        job,
                        entity,
                        paymentStatus);

                    job.StartProduction(
                        RequireTimestamp(
                            entity.ProductionStartedAt,
                            entity.Id,
                            nameof(entity.ProductionStartedAt)));
                    break;

                case JobProductionStatus.ReadyForPickup:
                    RestorePublishedAndSettlement(
                        job,
                        entity,
                        paymentStatus);

                    job.StartProduction(
                        RequireTimestamp(
                            entity.ProductionStartedAt,
                            entity.Id,
                            nameof(entity.ProductionStartedAt)));

                    job.MarkReadyForPickup(
                        RequireTimestamp(
                            entity.ReadyForPickupAt,
                            entity.Id,
                            nameof(entity.ReadyForPickupAt)));
                    break;

                case JobProductionStatus.Completed:
                    RestorePublishedAndSettlement(
                        job,
                        entity,
                        paymentStatus);

                    job.StartProduction(
                        RequireTimestamp(
                            entity.ProductionStartedAt,
                            entity.Id,
                            nameof(entity.ProductionStartedAt)));

                    job.MarkReadyForPickup(
                        RequireTimestamp(
                            entity.ReadyForPickupAt,
                            entity.Id,
                            nameof(entity.ReadyForPickupAt)));

                    job.Complete(
                        RequireTimestamp(
                            entity.CompletedAt,
                            entity.Id,
                            nameof(entity.CompletedAt)));
                    break;

                case JobProductionStatus.Cancelled:
                    if (entity.PublishedAt.HasValue)
                    {
                        job.Publish(
                            entity.PublishedAt.Value);
                    }

                    RestoreSettlement(
                        job,
                        entity,
                        paymentStatus);

                    job.Cancel(
                        RequireTimestamp(
                            entity.CancelledAt,
                            entity.Id,
                            nameof(entity.CancelledAt)));
                    break;

                default:
                    throw new InvalidDataException(
                        $"Zakázka '{entity.Id}' má neplatný " +
                        "výrobní stav.");
            }

            VerifyRestoredState(
                job,
                entity);

            return job;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
            when (
                exception is ArgumentException or
                InvalidOperationException or
                OverflowException)
        {
            throw new InvalidDataException(
                $"Zakázka '{entity.Id}' obsahuje " +
                "nekonzistentní data.",
                exception);
        }
    }

    private static void RestorePublishedAndSettlement(
        Job job,
        JobEntity entity,
        JobPaymentStatus paymentStatus)
    {
        job.Publish(
            RequireTimestamp(
                entity.PublishedAt,
                entity.Id,
                nameof(entity.PublishedAt)));

        RestoreSettlement(
            job,
            entity,
            paymentStatus);
    }

    private static void RestoreSettlement(
        Job job,
        JobEntity entity,
        JobPaymentStatus paymentStatus)
    {
        switch (paymentStatus)
        {
            case JobPaymentStatus.Unpaid:
                if (
                    entity.SettlementType is not null ||
                    entity.SettlementReferenceId is not null ||
                    entity.SettledAt is not null)
                {
                    throw new InvalidDataException(
                        $"Zakázka '{entity.Id}' má neplatné " +
                        "údaje neuhrazené zakázky.");
                }

                break;

            case JobPaymentStatus.Paid:
                if (
                    entity.SettlementType is null ||
                    entity.SettlementReferenceId is null ||
                    entity.SettledAt is null)
                {
                    throw new InvalidDataException(
                        $"Zakázka '{entity.Id}' nemá úplné " +
                        "údaje o úhradě.");
                }

                job.ConfirmSettlement(
                    (JobSettlementType)
                        entity.SettlementType.Value,
                    entity.SettlementReferenceId.Value,
                    entity.SettledAt.Value);

                break;

            default:
                throw new InvalidDataException(
                    $"Zakázka '{entity.Id}' má neplatný " +
                    "stav úhrady.");
        }
    }

    private static DateTimeOffset RequireTimestamp(
        DateTimeOffset? value,
        Guid jobId,
        string propertyName)
    {
        if (!value.HasValue)
        {
            throw new InvalidDataException(
                $"Zakázka '{jobId}' nemá povinný časový " +
                $"údaj '{propertyName}'.");
        }

        return value.Value;
    }

    private static void VerifyRestoredState(
        Job job,
        JobEntity entity)
    {
        var isConsistent =
            job.Id == entity.Id &&
            job.Number == entity.Number &&
            job.ServiceUnitId == entity.ServiceUnitId &&
            job.CustomerUserId == entity.CustomerUserId &&
            job.CreatedByUserId == entity.CreatedByUserId &&
            (int)job.ServiceType == entity.ServiceType &&
            job.Title == entity.Title &&
            job.Description == entity.Description &&
            job.Price.MinorUnits == entity.PriceMinorUnits &&
            (int)job.ProductionStatus ==
                entity.ProductionStatus &&
            (int)job.PaymentStatus ==
                entity.PaymentStatus &&
            (int?)job.SettlementType ==
                entity.SettlementType &&
            job.SettlementReferenceId ==
                entity.SettlementReferenceId &&
            job.CreatedAt == entity.CreatedAt &&
            job.PublishedAt == entity.PublishedAt &&
            job.SettledAt == entity.SettledAt &&
            job.ProductionStartedAt ==
                entity.ProductionStartedAt &&
            job.ReadyForPickupAt ==
                entity.ReadyForPickupAt &&
            job.CompletedAt == entity.CompletedAt &&
            job.CancelledAt == entity.CancelledAt;

        if (!isConsistent)
        {
            throw new InvalidDataException(
                $"Zakázka '{entity.Id}' nebyla z databázových " +
                "údajů obnovena beze ztráty informace.");
        }
    }

    private static JobEntity CreateEntity(
        Job job,
        long version)
    {
        return new JobEntity
        {
            Id = job.Id,
            Number = job.Number,
            ServiceUnitId = job.ServiceUnitId,
            CustomerUserId = job.CustomerUserId,
            CreatedByUserId = job.CreatedByUserId,
            ServiceType = (int)job.ServiceType,
            Title = job.Title,
            Description = job.Description,
            PriceMinorUnits = job.Price.MinorUnits,
            ProductionStatus =
                (int)job.ProductionStatus,
            PaymentStatus =
                (int)job.PaymentStatus,
            SettlementType =
                (int?)job.SettlementType,
            SettlementReferenceId =
                job.SettlementReferenceId,
            CreatedAt = job.CreatedAt,
            PublishedAt = job.PublishedAt,
            SettledAt = job.SettledAt,
            ProductionStartedAt =
                job.ProductionStartedAt,
            ReadyForPickupAt =
                job.ReadyForPickupAt,
            CompletedAt = job.CompletedAt,
            CancelledAt = job.CancelledAt,
            Version = version
        };
    }

    private static bool HasSettlement(Job job)
    {
        return
            job.SettlementType.HasValue &&
            job.SettlementReferenceId.HasValue;
    }

    private static JobSettlementReferenceAlreadyUsedException
        CreateSettlementReferenceException(
            Job job,
            Exception innerException)
    {
        return new JobSettlementReferenceAlreadyUsedException(
            job.SettlementType!.Value,
            job.SettlementReferenceId!.Value,
            innerException);
    }

    private static bool IsUniqueViolation(
        DbUpdateException exception,
        string constraintName)
    {
        return
            exception.InnerException is PostgresException
            {
                SqlState:
                    PostgresErrorCodes.UniqueViolation
            } postgresException &&
            postgresException.ConstraintName ==
                constraintName;
    }
}
