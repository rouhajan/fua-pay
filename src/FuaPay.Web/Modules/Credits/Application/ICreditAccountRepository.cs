using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public interface ICreditAccountRepository
{
    Task<CreditAccount?> FindByOwnerIdAsync(
        Guid ownerId,
        CancellationToken cancellationToken);

    Task AddAsync(
        CreditAccount account,
        CancellationToken cancellationToken);

    Task SaveAsync(
        CreditAccount account,
        CancellationToken cancellationToken);
}
