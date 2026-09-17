using FuaPay.Web.Modules.FinancialDocuments.Domain;

namespace FuaPay.Web.Modules.FinancialDocuments.Application;

public interface IFinancialDocumentPdfRenderer
{
    FinancialDocumentPdfFile Render(FinancialDocument document);
}

public sealed record FinancialDocumentPdfFile(
    byte[] Content,
    string FileName);
