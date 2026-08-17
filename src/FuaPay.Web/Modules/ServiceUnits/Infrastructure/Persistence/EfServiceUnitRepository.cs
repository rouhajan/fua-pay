using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace FuaPay.Web.Modules.ServiceUnits.Infrastructure.Persistence;

internal sealed class EfServiceUnitRepository :
    IServiceUnitRepository
{
    private readonly FuaPayDbContext _dbContext;
    private readonly Dictionary<Guid, long> _loadedVersions = [];

    public EfServiceUnitRepository(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<ServiceUnit?> FindByIdAsync(
        Guid serviceUnitId,
        CancellationToken cancellationToken)
    {
        if (serviceUnitId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID pracoviště nesmí být prázdné.",
                nameof(serviceUnitId));
        }

        var entity = await _dbContext.ServiceUnits
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == serviceUnitId,
                cancellationToken);

        return RestoreAndRemember(entity);
    }

    public async Task<ServiceUnit?> FindByCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeLookupCode(code);

        var entity = await _dbContext.ServiceUnits
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Code == normalized,
                cancellationToken);

        return RestoreAndRemember(entity);
    }

    public async Task AddAsync(
        ServiceUnit serviceUnit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceUnit);

        var entity = CreateEntity(serviceUnit, version: 1);
        _dbContext.ServiceUnits.Add(entity);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                ServiceUnitConfiguration.CodeUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new ServiceUnitCodeAlreadyUsedException(
                serviceUnit.Code,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                ServiceUnitConfiguration.PrimaryKeyConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new ServiceUnitConcurrencyException(
                serviceUnit.Id,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[serviceUnit.Id] = 1;
    }

    public async Task SaveAsync(
        ServiceUnit serviceUnit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceUnit);

        if (!_loadedVersions.TryGetValue(
            serviceUnit.Id,
            out var loadedVersion))
        {
            throw new InvalidOperationException(
                "Pracoviště musí být před uložením načteno " +
                "stejnou instancí repozitáře.");
        }

        var nextVersion = checked(loadedVersion + 1);
        var entity = CreateEntity(serviceUnit, nextVersion);

        _dbContext.Attach(entity);
        var entry = _dbContext.Entry(entity);

        entry.Property(item => item.DisplayName).IsModified = true;
        entry.Property(item => item.DefaultServiceType).IsModified = true;
        entry.Property(item => item.Status).IsModified = true;
        entry.Property(item => item.DeactivatedAt).IsModified = true;
        entry.Property(item => item.DeactivatedByType).IsModified = true;
        entry.Property(item => item.DeactivatedByUserId).IsModified = true;
        entry.Property(item => item.DeactivatedByProcessName).IsModified = true;
        entry.Property(item => item.Version).OriginalValue = loadedVersion;
        entry.Property(item => item.Version).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _dbContext.ChangeTracker.Clear();
            throw new ServiceUnitConcurrencyException(
                serviceUnit.Id,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
        _loadedVersions[serviceUnit.Id] = nextVersion;
    }

    private ServiceUnit? RestoreAndRemember(ServiceUnitEntity? entity)
    {
        if (entity is null)
        {
            return null;
        }

        var serviceUnit = Restore(entity);
        _loadedVersions[serviceUnit.Id] = entity.Version;
        return serviceUnit;
    }

    private static ServiceUnit Restore(ServiceUnitEntity entity)
    {
        if (entity.Version <= 0)
        {
            throw new InvalidDataException(
                $"Pracoviště '{entity.Id}' má neplatnou verzi.");
        }

        try
        {
            var serviceUnit = new ServiceUnit(
                entity.Id,
                entity.Code,
                entity.DisplayName,
                (ServiceType)entity.DefaultServiceType,
                entity.CreatedAt,
                RestoreActor(
                    entity.CreatedByType,
                    entity.CreatedByUserId,
                    entity.CreatedByProcessName));

            switch ((ServiceUnitStatus)entity.Status)
            {
                case ServiceUnitStatus.Active:
                    if (
                        entity.DeactivatedAt is not null ||
                        entity.DeactivatedByType is not null ||
                        entity.DeactivatedByUserId is not null ||
                        entity.DeactivatedByProcessName is not null)
                    {
                        throw new InvalidDataException(
                            $"Aktivní pracoviště '{entity.Id}' obsahuje " +
                            "údaje o deaktivaci.");
                    }
                    break;

                case ServiceUnitStatus.Inactive:
                    if (
                        entity.DeactivatedAt is null ||
                        entity.DeactivatedByType is null)
                    {
                        throw new InvalidDataException(
                            $"Neaktivní pracoviště '{entity.Id}' nemá " +
                            "úplné údaje o deaktivaci.");
                    }

                    serviceUnit.Deactivate(
                        entity.DeactivatedAt.Value,
                        RestoreActor(
                            entity.DeactivatedByType.Value,
                            entity.DeactivatedByUserId,
                            entity.DeactivatedByProcessName));
                    break;

                default:
                    throw new InvalidDataException(
                        $"Pracoviště '{entity.Id}' má neplatný stav.");
            }

            return serviceUnit;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
            when (
                exception is ArgumentException or
                InvalidOperationException)
        {
            throw new InvalidDataException(
                $"Pracoviště '{entity.Id}' obsahuje nekonzistentní data.",
                exception);
        }
    }

    private static ServiceUnitEntity CreateEntity(
        ServiceUnit serviceUnit,
        long version)
    {
        var entity = new ServiceUnitEntity
        {
            Id = serviceUnit.Id,
            Code = serviceUnit.Code,
            DisplayName = serviceUnit.DisplayName,
            DefaultServiceType = (int)serviceUnit.DefaultServiceType,
            Status = (int)serviceUnit.Status,
            CreatedAt = serviceUnit.CreatedAt,
            DeactivatedAt = serviceUnit.DeactivatedAt,
            Version = version
        };

        ApplyActor(entity, serviceUnit.CreatedBy, created: true);
        ApplyActor(entity, serviceUnit.DeactivatedBy, created: false);
        return entity;
    }

    private static void ApplyActor(
        ServiceUnitEntity entity,
        ServiceUnitChangeActor? actor,
        bool created)
    {
        if (created && actor is null)
        {
            throw new InvalidOperationException(
                "Pracoviště musí mít původce vytvoření.");
        }

        if (created)
        {
            entity.CreatedByType = (int)actor!.Type;
            entity.CreatedByUserId = actor.UserId;
            entity.CreatedByProcessName = actor.ProcessName;
            return;
        }

        entity.DeactivatedByType = actor is null
            ? null
            : (int)actor.Type;
        entity.DeactivatedByUserId = actor?.UserId;
        entity.DeactivatedByProcessName = actor?.ProcessName;
    }

    internal static ServiceUnitChangeActor RestoreActor(
        int actorType,
        Guid? userId,
        string? processName)
    {
        return (ServiceUnitChangeActorType)actorType switch
        {
            ServiceUnitChangeActorType.User
                when userId.HasValue &&
                     userId.Value != Guid.Empty &&
                     processName is null =>
                ServiceUnitChangeActor.ForUser(userId.Value),

            ServiceUnitChangeActorType.Process
                when userId is null &&
                     !string.IsNullOrWhiteSpace(processName) =>
                ServiceUnitChangeActor.ForProcess(processName),

            _ => throw new InvalidDataException(
                "Původce změny pracoviště má neplatnou podobu.")
        };
    }

    private static string NormalizeLookupCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException(
                "Kód pracoviště nesmí být prázdný.",
                nameof(code));
        }

        return code.Trim().ToUpperInvariant();
    }

    private static bool IsUniqueViolation(
        DbUpdateException exception,
        string constraintName)
    {
        return
            exception.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
            string.Equals(
                postgres.ConstraintName,
                constraintName,
                StringComparison.Ordinal);
    }
}
