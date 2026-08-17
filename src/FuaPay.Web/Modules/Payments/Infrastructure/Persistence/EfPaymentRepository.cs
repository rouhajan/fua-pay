using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class EfPaymentRepository : IPaymentRepository
{
    private static readonly int[] BlockingStatusValues =
        JobPaymentBlockingPolicy.Statuses
            .Select(status => (int)status)
            .ToArray();

    private readonly FuaPayDbContext _dbContext;
    private readonly Dictionary<Guid, long> _loadedVersions = [];

    public EfPaymentRepository(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<Payment?> FindByIdAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(paymentId, nameof(paymentId));

        var entity = await _dbContext.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == paymentId,
                cancellationToken);

        return entity is null ? null : Remember(Restore(entity), entity.Version);
    }

    public async Task<Payment?> FindBlockingForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(jobId, nameof(jobId));

        var entity = await _dbContext.Payments
            .AsNoTracking()
            .Where(
                item =>
                    item.JobId == jobId &&
                    BlockingStatusValues.Contains(item.Status))
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : Remember(Restore(entity), entity.Version);
    }

    public async Task<Payment?> FindByProviderReferenceAsync(
        PaymentProvider provider,
        string providerReference,
        CancellationToken cancellationToken = default)
    {
        if (
            provider == PaymentProvider.Unknown ||
            !Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(
                nameof(provider),
                "Poskytovatel platby není podporovaný.");
        }

        var normalizedReference =
            PaymentProviderReference.Normalize(
                providerReference,
                nameof(providerReference));

        var entity = await _dbContext.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.Provider == (int)provider &&
                    item.ProviderReference == normalizedReference,
                cancellationToken);

        return entity is null
            ? null
            : Remember(Restore(entity), entity.Version);
    }

    public async Task<Payment?> FindByCreationRequestIdAsync(
        Guid creationRequestId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(creationRequestId, nameof(creationRequestId));

        var entity = await _dbContext.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.CreationRequestId == creationRequestId,
                cancellationToken);

        return entity is null
            ? null
            : Remember(Restore(entity), entity.Version);
    }

    public async Task AddAsync(
        Payment payment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payment);

        var entity = ToEntity(payment, version: 1);
        _dbContext.Payments.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (
                payment.CreationRequestId.HasValue &&
                IsUniqueViolation(
                    exception,
                    PaymentConfiguration.CreationRequestUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new PaymentCreationRequestAlreadyExistsException(
                payment.CreationRequestId.Value,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(exception))
        {
            _dbContext.ChangeTracker.Clear();
            throw new PaymentConcurrencyException(payment.Id, exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[payment.Id] = entity.Version;
    }

    public async Task AddPreparedAsync(
        Payment payment,
        PaymentInitiation initiation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(initiation);

        if (
            initiation.PaymentId != payment.Id ||
            initiation.Provider != payment.Provider)
        {
            throw new ArgumentException(
                "Inicializace neodpovídá připravované platbě.",
                nameof(initiation));
        }

        if (payment.Status != PaymentStatus.Created)
        {
            throw new InvalidOperationException(
                "Připravit lze pouze novou platbu ve stavu Created.");
        }

        var paymentEntity = ToEntity(payment, version: 1);
        var initiationEntity = EfPaymentInitiationRepository.ToEntity(
            initiation,
            version: 1);

        _dbContext.Payments.Add(paymentEntity);
        _dbContext.PaymentInitiations.Add(initiationEntity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (
                payment.CreationRequestId.HasValue &&
                IsUniqueViolation(
                    exception,
                    PaymentConfiguration.CreationRequestUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new PaymentCreationRequestAlreadyExistsException(
                payment.CreationRequestId.Value,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(exception))
        {
            _dbContext.ChangeTracker.Clear();
            throw new PaymentConcurrencyException(payment.Id, exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[payment.Id] = paymentEntity.Version;
    }

    public async Task SaveAsync(
        Payment payment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payment);

        if (!_loadedVersions.TryGetValue(payment.Id, out var loadedVersion))
        {
            throw new InvalidOperationException(
                "Platba musí být před uložením načtena stejným repozitářem.");
        }

        var nextVersion = checked(loadedVersion + 1);
        var entity = ToEntity(payment, nextVersion);
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
            throw new PaymentConcurrencyException(payment.Id, exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(exception))
        {
            _dbContext.ChangeTracker.Clear();
            throw new PaymentConcurrencyException(payment.Id, exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[payment.Id] = nextVersion;
    }

    private Payment Remember(Payment payment, long version)
    {
        _loadedVersions[payment.Id] = version;
        return payment;
    }

    private static Payment Restore(PaymentEntity entity)
    {
        if (entity.Version <= 0)
        {
            throw new InvalidDataException(
                $"Platba '{entity.Id}' má neplatnou verzi.");
        }

        var payment = new Payment(
            entity.Id,
            entity.CustomerUserId,
            (PaymentPurposeType)entity.PurposeType,
            entity.JobId,
            new Money(entity.AmountMinorUnits),
            (PaymentProvider)entity.Provider,
            entity.CreatedAt,
            entity.CreationRequestId);

        var status = (PaymentStatus)entity.Status;

        if (status != PaymentStatus.Created)
        {
            payment.MarkPending(
                entity.ProviderReference
                    ?? throw new InvalidDataException(
                        $"Platba '{entity.Id}' nemá referenci poskytovatele."),
                entity.UpdatedAt);
        }

        var completedAt = entity.CompletedAt ?? entity.UpdatedAt;

        switch (status)
        {
            case PaymentStatus.Created:
            case PaymentStatus.Pending:
                break;
            case PaymentStatus.Succeeded:
                payment.Complete(completedAt);
                break;
            case PaymentStatus.Failed:
                payment.Fail(
                    entity.FailureReason
                        ?? throw new InvalidDataException(
                            $"Neúspěšná platba '{entity.Id}' nemá důvod."),
                    completedAt);
                break;
            case PaymentStatus.Cancelled:
                payment.Cancel(completedAt);
                break;
            case PaymentStatus.Expired:
                payment.Expire(completedAt);
                break;
            default:
                throw new InvalidDataException(
                    $"Platba '{entity.Id}' má nepodporovaný stav '{status}'.");
        }

        return payment;
    }

    private static PaymentEntity ToEntity(Payment payment, long version)
    {
        return new PaymentEntity
        {
            Id = payment.Id,
            CustomerUserId = payment.CustomerUserId,
            PurposeType = (int)payment.PurposeType,
            JobId = payment.JobId,
            AmountMinorUnits = payment.Amount.MinorUnits,
            Provider = (int)payment.Provider,
            CreationRequestId = payment.CreationRequestId,
            Status = (int)payment.Status,
            ProviderReference = payment.ProviderReference,
            FailureReason = payment.FailureReason,
            CreatedAt = payment.CreatedAt,
            UpdatedAt = payment.UpdatedAt,
            CompletedAt = payment.CompletedAt,
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
                "ID nesmí být prázdné.",
                parameterName);
        }
    }
}
