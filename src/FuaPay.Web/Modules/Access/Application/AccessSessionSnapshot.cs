using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Application;

public sealed record AccessSessionSnapshot(
    Guid UserId,
    string DisplayName,
    string? Email,
    AccessUserStatus Status,
    IReadOnlyCollection<AccessRole> Roles);
