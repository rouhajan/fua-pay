using FuaPay.Web.Modules.FinancialDocuments.Domain;

namespace FuaPay.Web.Modules.FinancialDocuments.Application;

public interface IFinancialDocumentArchiveQueries
{
    Task<IReadOnlyList<FinancialDocument>> ListIssuedAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        int limit,
        CancellationToken cancellationToken);
}
