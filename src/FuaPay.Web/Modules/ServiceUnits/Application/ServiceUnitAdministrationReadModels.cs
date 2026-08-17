using FuaPay.Web.Modules.ServiceUnits.Domain;

namespace FuaPay.Web.Modules.ServiceUnits.Application;

public sealed record ServiceUnitAdministrationListItem(
    Guid Id,
    string Code,
    string DisplayName,
    ServiceType DefaultServiceType,
    ServiceUnitStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeactivatedAt,
    long ActiveRequesterCount);

public sealed record RequesterServiceUnitAssignmentReadModel(
    Guid Id,
    Guid ServiceUnitId,
    string ServiceUnitCode,
    string ServiceUnitDisplayName,
    Guid UserId,
    DateTimeOffset GrantedAt,
    DateTimeOffset? RevokedAt)
{
    public bool IsActive => RevokedAt is null;
}
