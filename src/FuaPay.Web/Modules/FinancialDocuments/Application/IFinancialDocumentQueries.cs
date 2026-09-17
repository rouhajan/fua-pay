using FuaPay.Web.Modules.FinancialDocuments.Domain;

namespace FuaPay.Web.Modules.FinancialDocuments.Application;

public interface IFinancialDocumentQueries
{
    Task<FinancialDocument?> FindByIdForCustomerAsync(
        Guid documentId,
        Guid customerUserId,
        CancellationToken cancellationToken = default);

    Task<FinancialDocument?> FindByIdForAdminAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
