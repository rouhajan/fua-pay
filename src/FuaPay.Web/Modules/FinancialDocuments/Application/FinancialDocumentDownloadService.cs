namespace FuaPay.Web.Modules.FinancialDocuments.Application;

public sealed class FinancialDocumentDownloadService
{
    private readonly IFinancialDocumentQueries _queries;
    private readonly IFinancialDocumentPdfRenderer _renderer;

    public FinancialDocumentDownloadService(
        IFinancialDocumentQueries queries,
        IFinancialDocumentPdfRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(renderer);
        _queries = queries;
        _renderer = renderer;
    }

    public async Task<FinancialDocumentPdfFile?> GetForCustomerAsync(
        Guid documentId,
        Guid customerUserId,
        CancellationToken cancellationToken = default)
    {
        var document = await _queries.FindByIdForCustomerAsync(
            documentId,
            customerUserId,
            cancellationToken);

        return document is null ? null : _renderer.Render(document);
    }

    public async Task<FinancialDocumentPdfFile?> GetForAdminAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _queries.FindByIdForAdminAsync(
            documentId,
            cancellationToken);

        return document is null ? null : _renderer.Render(document);
    }
}
