namespace FuaPay.Web.Modules.FinancialDocuments.Infrastructure.Persistence;

internal sealed class FinancialDocumentNumberCounterEntity
{
    public int BusinessYear { get; set; }

    public int LastValue { get; set; }
}
