using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class EfPaymentInitiationRepository :
    IPaymentInitiationRepository
{
    private readonly FuaPayDbContext _dbContext;
    private readonly Dictionary<Guid, long> _loadedVersions = [];

    public EfPaymentInitiationRepository(
        FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<PaymentInitiation?> FindByPaymentIdAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID platby nesmí být prázdné.",
                nameof(paymentId));
        }

        var entity = await _dbContext.PaymentInitiations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PaymentId == paymentId,
                cancellationToken);

        return entity is null
            ? null
            : Remember(Restore(entity), entity.Version);
    }

    public async Task SaveAsync(
        PaymentInitiation initiation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initiation);

        if (
            !_loadedVersions.TryGetValue(
                initiation.PaymentId,
                out var loadedVersion))
        {
            throw new InvalidOperationException(
                "Inicializace platby musí být před uložením načtena stejným repozitářem.");
        }

        var nextVersion = checked(loadedVersion + 1);
        var entity = ToEntity(initiation, nextVersion);
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
            throw new PaymentConcurrencyException(
                initiation.PaymentId,
                exception);
        }
        catch (DbUpdateException exception)
        {
            _dbContext.ChangeTracker.Clear();
            throw new PaymentConcurrencyException(
                initiation.PaymentId,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[initiation.PaymentId] = nextVersion;
    }

    internal static PaymentInitiationEntity ToEntity(
        PaymentInitiation initiation,
        long version)
    {
        return new PaymentInitiationEntity
        {
            PaymentId = initiation.PaymentId,
            Provider = (int)initiation.Provider,
            OrderNumber = initiation.OrderNumber,
            CorrelationId = initiation.CorrelationId,
            State = (int)initiation.State,
            LastError = initiation.LastError,
            ProcessUri = initiation.ProcessUri?.AbsoluteUri,
            ObservedProviderReference = initiation.ObservedProviderReference,
            ObservedProcessUri = initiation.ObservedProcessUri?.AbsoluteUri,
            CreatedAt = initiation.CreatedAt,
            UpdatedAt = initiation.UpdatedAt,
            StartedAt = initiation.StartedAt,
            FinishedAt = initiation.FinishedAt,
            Version = version
        };
    }

    private PaymentInitiation Remember(
        PaymentInitiation initiation,
        long version)
    {
        _loadedVersions[initiation.PaymentId] = version;
        return initiation;
    }

    private static PaymentInitiation Restore(
        PaymentInitiationEntity entity)
    {
        if (entity.Version <= 0)
        {
            throw new InvalidDataException(
                $"Inicializace platby '{entity.PaymentId}' má neplatnou verzi.");
        }

        var initiation = new PaymentInitiation(
            entity.PaymentId,
            (PaymentProvider)entity.Provider,
            entity.OrderNumber,
            entity.CorrelationId,
            entity.CreatedAt);
        var state = (PaymentInitiationState)entity.State;

        switch (state)
        {
            case PaymentInitiationState.Prepared:
                break;
            case PaymentInitiationState.InProgress:
                initiation.Begin(
                    entity.StartedAt
                        ?? throw MissingTimestamp(entity, "started_at"));
                break;
            case PaymentInitiationState.Initialized:
                initiation.Begin(
                    entity.StartedAt
                        ?? throw MissingTimestamp(entity, "started_at"));
                initiation.Complete(
                    entity.FinishedAt
                        ?? throw MissingTimestamp(entity, "finished_at"),
                    ParseProcessUri(entity));
                break;
            case PaymentInitiationState.Uncertain:
                initiation.Begin(
                    entity.StartedAt
                        ?? throw MissingTimestamp(entity, "started_at"));
                initiation.MarkUncertain(
                    entity.LastError
                        ?? throw new InvalidDataException(
                            $"Nejasná inicializace platby '{entity.PaymentId}' nemá důvod."),
                    entity.FinishedAt
                        ?? throw MissingTimestamp(entity, "finished_at"));

                if (entity.ObservedProviderReference is not null)
                {
                    initiation.RecordObservedProviderResult(
                        entity.ObservedProviderReference,
                        ParseOptionalProcessUri(
                            entity.PaymentId,
                            entity.ObservedProcessUri,
                            "observed_process_uri"),
                        entity.UpdatedAt);
                }

                break;
            default:
                throw new InvalidDataException(
                    $"Inicializace platby '{entity.PaymentId}' má nepodporovaný stav '{state}'.");
        }

        if (initiation.UpdatedAt != entity.UpdatedAt)
        {
            throw new InvalidDataException(
                $"Inicializace platby '{entity.PaymentId}' má nekonzistentní čas poslední změny.");
        }

        var restoredEntity = ToEntity(initiation, entity.Version);

        if (
            restoredEntity.PaymentId != entity.PaymentId ||
            restoredEntity.Provider != entity.Provider ||
            restoredEntity.OrderNumber != entity.OrderNumber ||
            restoredEntity.CorrelationId != entity.CorrelationId ||
            restoredEntity.State != entity.State ||
            restoredEntity.LastError != entity.LastError ||
            restoredEntity.ProcessUri != entity.ProcessUri ||
            restoredEntity.ObservedProviderReference !=
                entity.ObservedProviderReference ||
            restoredEntity.ObservedProcessUri != entity.ObservedProcessUri ||
            restoredEntity.CreatedAt != entity.CreatedAt ||
            restoredEntity.StartedAt != entity.StartedAt ||
            restoredEntity.FinishedAt != entity.FinishedAt ||
            restoredEntity.Version != entity.Version)
        {
            throw new InvalidDataException(
                $"Payment initiation '{entity.PaymentId}' has an inconsistent persisted state.");
        }

        return initiation;
    }

    private static Uri? ParseProcessUri(
        PaymentInitiationEntity entity) =>
        ParseOptionalProcessUri(
            entity.PaymentId,
            entity.ProcessUri,
            "process_uri");

    private static Uri? ParseOptionalProcessUri(
        Guid paymentId,
        string? value,
        string columnName)
    {
        if (value is null)
        {
            return null;
        }

        if (
            !Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var processUri) ||
            processUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException(
                $"Payment initiation '{paymentId}' has an invalid {columnName} URI.");
        }

        return processUri;
    }

    private static InvalidDataException MissingTimestamp(
        PaymentInitiationEntity entity,
        string columnName) =>
        new(
            $"Inicializace platby '{entity.PaymentId}' nemá povinný čas '{columnName}'.");
}
