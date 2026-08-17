using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.ServiceUnits.Domain;

namespace FuaPay.Web.Modules.ServiceUnits.Application;

public sealed class ServiceUnitAdministrationService
{
    private readonly IServiceUnitRepository _serviceUnits;
    private readonly IRequesterServiceUnitAssignmentRepository
        _assignments;
    private readonly IAccessUserQueries _accessUsers;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditTrail _auditTrail;

    public ServiceUnitAdministrationService(
        IServiceUnitRepository serviceUnits,
        IRequesterServiceUnitAssignmentRepository assignments,
        IAccessUserQueries accessUsers,
        TimeProvider timeProvider,
        IAuditTrail auditTrail)
    {
        ArgumentNullException.ThrowIfNull(serviceUnits);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(accessUsers);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(auditTrail);

        _serviceUnits = serviceUnits;
        _assignments = assignments;
        _accessUsers = accessUsers;
        _timeProvider = timeProvider;
        _auditTrail = auditTrail;
    }

    public async Task<ServiceUnit> CreateAsync(
        Guid serviceUnitId,
        string code,
        string displayName,
        ServiceType defaultServiceType,
        ServiceUnitChangeActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var now = _timeProvider.GetUtcNow();
        var serviceUnit = new ServiceUnit(
            serviceUnitId,
            code,
            displayName,
            defaultServiceType,
            now,
            actor);

        var existing = await _serviceUnits.FindByCodeAsync(
            serviceUnit.Code,
            cancellationToken);

        if (existing is not null)
        {
            throw new ServiceUnitCodeAlreadyUsedException(
                serviceUnit.Code);
        }

        _auditTrail.Stage(CreateAuditEntry(
            actor,
            "service-unit.created",
            "service-unit",
            serviceUnit.Id.ToString(),
            $"Pracoviště {serviceUnit.Code} – {serviceUnit.DisplayName} bylo vytvořeno.",
            now));

        await _serviceUnits.AddAsync(
            serviceUnit,
            cancellationToken);

        return serviceUnit;
    }

    public async Task<ServiceUnit> UpdateDetailsAsync(
        Guid serviceUnitId,
        string displayName,
        ServiceType defaultServiceType,
        ServiceUnitChangeActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var serviceUnit = await RequireServiceUnitAsync(
            serviceUnitId,
            cancellationToken);

        serviceUnit.UpdateDetails(
            displayName,
            defaultServiceType);

        var now = _timeProvider.GetUtcNow();
        _auditTrail.Stage(CreateAuditEntry(
            actor,
            "service-unit.updated",
            "service-unit",
            serviceUnit.Id.ToString(),
            $"Pracoviště {serviceUnit.Code} bylo upraveno.",
            now));

        await _serviceUnits.SaveAsync(
            serviceUnit,
            cancellationToken);

        return serviceUnit;
    }

    public async Task<RequesterServiceUnitAssignment> AssignRequesterAsync(
        Guid assignmentId,
        Guid serviceUnitId,
        Guid userId,
        ServiceUnitChangeActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        await RequireRequesterAsync(
            userId,
            cancellationToken);

        var serviceUnit = await RequireServiceUnitAsync(
            serviceUnitId,
            cancellationToken);

        if (!serviceUnit.IsActive)
        {
            throw new InactiveServiceUnitException(serviceUnitId);
        }

        var existing = await _assignments.FindActiveAsync(
            serviceUnitId,
            userId,
            cancellationToken);

        if (existing is not null)
        {
            throw new RequesterAlreadyAssignedException(
                serviceUnitId,
                userId);
        }

        var now = _timeProvider.GetUtcNow();
        var assignment = new RequesterServiceUnitAssignment(
            assignmentId,
            serviceUnitId,
            userId,
            now,
            actor);

        _auditTrail.Stage(CreateAuditEntry(
            actor,
            "service-unit.requester-assigned",
            "requester-service-unit-assignment",
            assignment.Id.ToString(),
            $"Uživatel {userId} byl přiřazen k pracovišti {serviceUnit.Code}.",
            now));

        await _assignments.AddAsync(
            assignment,
            cancellationToken);

        return assignment;
    }

    public async Task RevokeRequesterAsync(
        Guid serviceUnitId,
        Guid userId,
        ServiceUnitChangeActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var assignment = await _assignments.FindActiveAsync(
            serviceUnitId,
            userId,
            cancellationToken)
            ?? throw new RequesterAssignmentNotFoundException(
                serviceUnitId,
                userId);

        var now = _timeProvider.GetUtcNow();
        assignment.Revoke(
            now,
            actor);

        _auditTrail.Stage(CreateAuditEntry(
            actor,
            "service-unit.requester-revoked",
            "requester-service-unit-assignment",
            assignment.Id.ToString(),
            $"Přiřazení uživatele {userId} k pracovišti {serviceUnitId} bylo odebráno.",
            now));

        await _assignments.SaveAsync(
            assignment,
            cancellationToken);
    }

    public async Task DeactivateAsync(
        Guid serviceUnitId,
        ServiceUnitChangeActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var serviceUnit = await RequireServiceUnitAsync(
            serviceUnitId,
            cancellationToken);

        var now = _timeProvider.GetUtcNow();
        serviceUnit.Deactivate(
            now,
            actor);

        _auditTrail.Stage(CreateAuditEntry(
            actor,
            "service-unit.deactivated",
            "service-unit",
            serviceUnit.Id.ToString(),
            $"Pracoviště {serviceUnit.Code} bylo deaktivováno.",
            now));

        await _serviceUnits.SaveAsync(
            serviceUnit,
            cancellationToken);
    }

    private static AuditEntry CreateAuditEntry(
        ServiceUnitChangeActor actor,
        string action,
        string entityType,
        string entityId,
        string description,
        DateTimeOffset occurredAt)
    {
        return actor.UserId.HasValue
            ? AuditEntry.ForUser(
                actor.UserId.Value,
                action,
                entityType,
                entityId,
                description,
                occurredAt)
            : AuditEntry.ForProcess(
                actor.ProcessName!,
                action,
                entityType,
                entityId,
                description,
                occurredAt);
    }

    private async Task RequireRequesterAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(userId));
        }

        var user = await _accessUsers.FindDetailAsync(
            userId,
            cancellationToken)
            ?? throw new AccessUserNotFoundException(userId);

        if (user.Status == AccessUserStatus.Blocked)
        {
            throw new AccessUserBlockedException(userId);
        }

        if (!user.ActiveRoles.Contains(AccessRole.Requester))
        {
            throw new RequesterRoleRequiredException(userId);
        }
    }

    private async Task<ServiceUnit> RequireServiceUnitAsync(
        Guid serviceUnitId,
        CancellationToken cancellationToken)
    {
        if (serviceUnitId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID pracoviště nesmí být prázdné.",
                nameof(serviceUnitId));
        }

        return await _serviceUnits.FindByIdAsync(
                serviceUnitId,
                cancellationToken)
            ?? throw new ServiceUnitNotFoundException(
                serviceUnitId);
    }
}
