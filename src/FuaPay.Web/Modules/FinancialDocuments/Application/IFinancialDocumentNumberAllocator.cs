namespace FuaPay.Web.Modules.FinancialDocuments.Application;

public interface IFinancialDocumentNumberAllocator
{
    Task<FinancialDocumentNumberAllocation> AllocateAsync(
        DateTimeOffset issuedAt,
        CancellationToken cancellationToken = default);
}

public sealed record FinancialDocumentNumberAllocation(
    string DocumentNumber,
    int BusinessYear,
    int Sequence);
