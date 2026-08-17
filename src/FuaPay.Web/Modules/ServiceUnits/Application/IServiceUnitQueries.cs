namespace FuaPay.Web.Modules.ServiceUnits.Application;

public interface IServiceUnitQueries
{
    Task<IReadOnlyList<ServiceUnitAdministrationListItem>> ListAllAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RequesterServiceUnitAssignmentReadModel>>
        ListAssignmentsForUserAsync(
            Guid userId,
            bool includeRevoked = false,
            CancellationToken cancellationToken = default);

    Task<ServiceUnitReadModel?> FindActiveAsync(
        Guid serviceUnitId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceUnitReadModel>> ListActiveAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceUnitReadModel>> ListForRequesterAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
