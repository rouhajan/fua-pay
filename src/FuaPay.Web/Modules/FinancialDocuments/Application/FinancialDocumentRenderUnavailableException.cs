namespace FuaPay.Web.Modules.FinancialDocuments.Application;

public enum FinancialDocumentRenderUnavailableReason
{
    LegacyIncompleteSnapshot = 1,
    UnsupportedVersion = 2,
    UnsupportedDocumentType = 3,
    LayoutOverflow = 4
}

public sealed class FinancialDocumentRenderUnavailableException : Exception
{
    public FinancialDocumentRenderUnavailableException(
        FinancialDocumentRenderUnavailableReason reason,
        string message)
        : base(message)
    {
        Reason = reason;
    }

    public FinancialDocumentRenderUnavailableReason Reason { get; }
}
