using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;

namespace FuaPay.Web.Tests.Modules.FinancialDocuments.Application;

public sealed class FinancialDocumentDownloadServiceTests
{
    [Fact]
    public async Task RepeatedDownloadOnlyQueriesAndRendersExistingSnapshot()
    {
        var document = FinancialDocumentPdfRendererTests.CreateDocument(
            FinancialDocumentType.ManualCreditTopUp);
        var queries = new RecordingQueries(document);
        var renderer = new RecordingRenderer();
        var service = new FinancialDocumentDownloadService(queries, renderer);

        var first = await service.GetForCustomerAsync(
            document.DocumentId,
            document.Customer.CustomerUserId);
        var second = await service.GetForCustomerAsync(
            document.DocumentId,
            document.Customer.CustomerUserId);

        Assert.Equal(first, second);
        Assert.Equal(2, queries.CustomerReads);
        Assert.Equal(2, renderer.RenderCount);
        Assert.Same(document, renderer.LastDocument);
    }

    [Fact]
    public async Task CustomerOwnershipAndAdminLookupUseSeparateQueries()
    {
        var document = FinancialDocumentPdfRendererTests.CreateDocument(
            FinancialDocumentType.ManualCreditTopUp);
        var queries = new RecordingQueries(document);
        var service = new FinancialDocumentDownloadService(
            queries,
            new RecordingRenderer());

        var foreign = await service.GetForCustomerAsync(
            document.DocumentId,
            Guid.NewGuid());
        var admin = await service.GetForAdminAsync(document.DocumentId);

        Assert.Null(foreign);
        Assert.NotNull(admin);
        Assert.Equal(1, queries.CustomerReads);
        Assert.Equal(1, queries.AdminReads);
    }

    private sealed class RecordingQueries : IFinancialDocumentQueries
    {
        private readonly FinancialDocument _document;

        public RecordingQueries(FinancialDocument document) =>
            _document = document;

        public int CustomerReads { get; private set; }

        public int AdminReads { get; private set; }

        public Task<IReadOnlyDictionary<Guid, Guid>>
            FindDocumentIdsBySourceForCustomerAsync(
                Guid customerUserId,
                FinancialDocumentSourceType sourceType,
                IEnumerable<Guid> sourceIds,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(
                new Dictionary<Guid, Guid>());

        public Task<FinancialDocument?> FindByIdForCustomerAsync(
            Guid documentId,
            Guid customerUserId,
            CancellationToken cancellationToken = default)
        {
            CustomerReads++;
            return Task.FromResult<FinancialDocument?>(
                documentId == _document.DocumentId &&
                customerUserId == _document.Customer.CustomerUserId
                    ? _document
                    : null);
        }

        public Task<FinancialDocument?> FindByIdForAdminAsync(
            Guid documentId,
            CancellationToken cancellationToken = default)
        {
            AdminReads++;
            return Task.FromResult<FinancialDocument?>(
                documentId == _document.DocumentId ? _document : null);
        }
    }

    private sealed class RecordingRenderer : IFinancialDocumentPdfRenderer
    {
        private readonly FinancialDocumentPdfFile _file =
            new([0x25, 0x50, 0x44, 0x46], "document.pdf");

        public int RenderCount { get; private set; }

        public FinancialDocument? LastDocument { get; private set; }

        public FinancialDocumentPdfFile Render(FinancialDocument document)
        {
            RenderCount++;
            LastDocument = document;
            return _file;
        }
    }
}
