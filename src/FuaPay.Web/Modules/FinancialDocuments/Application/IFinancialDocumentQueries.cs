using FuaPay.Web.Modules.FinancialDocuments.Domain;

namespace FuaPay.Web.Modules.FinancialDocuments.Application;

public interface IFinancialDocumentQueries
{
    Task<IReadOnlyDictionary<Guid, Guid>>
        FindDocumentIdsBySourceForCustomerAsync(
            Guid customerUserId,
            FinancialDocumentSourceType sourceType,
            IEnumerable<Guid> sourceIds,
            CancellationToken cancellationToken = default);

    Task<FinancialDocument?> FindByIdForCustomerAsync(
        Guid documentId,
        Guid customerUserId,
        CancellationToken cancellationToken = default);

    Task<FinancialDocument?> FindByIdForAdminAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
