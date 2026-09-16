using FuaPay.Web.Modules.FinancialDocuments.Domain;

namespace FuaPay.Web.Modules.FinancialDocuments.Application;

public interface IFinancialDocumentRepository
{
    Task<FinancialDocument?> FindBySourceAsync(
        FinancialDocumentSourceType sourceType,
        Guid sourceId,
        CancellationToken cancellationToken = default);

    void Stage(FinancialDocument document);
}
