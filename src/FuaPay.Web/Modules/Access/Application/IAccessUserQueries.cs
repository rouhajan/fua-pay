using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Application;

public interface IAccessUserQueries
{
    Task<AccessUserPage> ListAsync(
        AccessUserListRequest request,
        CancellationToken cancellationToken = default);

    Task<AccessUserDetail?> FindDetailAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccessUserOption>> ListActiveCustomersAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, AccessUserOption>> FindOptionsAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken = default);

    Task<bool> IsActiveAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> IsActiveCustomerAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<long> CountActiveUsersWithRoleAsync(
        AccessRole role,
        CancellationToken cancellationToken = default);
}
