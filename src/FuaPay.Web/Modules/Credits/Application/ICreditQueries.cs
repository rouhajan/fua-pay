namespace FuaPay.Web.Modules.Credits.Application;

public interface ICreditQueries
{
    Task<CreditAdministrationMovementPage> ListAdministrationMovementsAsync(
        CreditAdministrationMovementFilter filter,
        CreditMovementPageRequest page,
        CancellationToken cancellationToken = default);

    Task<CreditAccountSummary?> FindAccountForOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default);

    Task<CreditMovementPage> ListMovementsForOwnerAsync(
        Guid ownerId,
        CreditMovementPageRequest page,
        CancellationToken cancellationToken = default);
}
