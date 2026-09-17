using System.Security.Claims;

using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;
using FuaPay.Web.Tests.Modules.FinancialDocuments.Application;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using AdminDownloadModel =
    FuaPay.Web.Pages.Admin.FinancialDocuments.DownloadModel;
using CustomerDownloadModel =
    FuaPay.Web.Pages.Customer.FinancialDocuments.DownloadModel;

namespace FuaPay.Web.Tests.Pages;

public sealed class FinancialDocumentDownloadPageTests
{
    [Fact]
    public void PagesRequireTheirExactRoles()
    {
        Assert.Equal(
            "Customer",
            Assert.Single(typeof(CustomerDownloadModel)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>()).Roles);
        Assert.Equal(
            "Admin",
            Assert.Single(typeof(AdminDownloadModel)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>()).Roles);
    }

    [Fact]
    public async Task CustomerForeignOrMissingDocumentReturns404WithoutRendering()
    {
        var document = FinancialDocumentPdfRendererTests.CreateDocument(
            FinancialDocumentType.ManualCreditTopUp);
        var renderer = new StubRenderer();
        var model = CustomerModel(
            new StubQueries(document),
            renderer,
            Guid.NewGuid());
        var ownerModel = CustomerModel(
            new StubQueries(document),
            renderer,
            document.Customer.CustomerUserId);

        var foreign = await model.OnGetAsync(document.DocumentId);
        var missing = await ownerModel.OnGetAsync(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(foreign);
        Assert.IsType<NotFoundResult>(missing);
        Assert.Equal(0, renderer.Count);
    }

    [Fact]
    public async Task CustomerOwnerAndAdminReceivePrivatePdf()
    {
        var document = FinancialDocumentPdfRendererTests.CreateDocument(
            FinancialDocumentType.ManualCreditTopUp);
        var queries = new StubQueries(document);
        var customer = CustomerModel(
            queries,
            new StubRenderer(),
            document.Customer.CustomerUserId);
        var admin = AdminModel(queries, new StubRenderer());

        var customerResult = Assert.IsType<FileContentResult>(
            await customer.OnGetAsync(document.DocumentId));
        var adminResult = Assert.IsType<FileContentResult>(
            await admin.OnGetAsync(document.DocumentId));

        Assert.Equal("application/pdf", customerResult.ContentType);
        Assert.Equal("document.pdf", customerResult.FileDownloadName);
        Assert.Equal("private, no-store", customer.Response.Headers.CacheControl);
        Assert.Equal("application/pdf", adminResult.ContentType);
        Assert.Equal("private, no-store", admin.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task AdminMissingDocumentReturns404()
    {
        var document = FinancialDocumentPdfRendererTests.CreateDocument(
            FinancialDocumentType.ManualCreditTopUp);
        var model = AdminModel(new StubQueries(document), new StubRenderer());

        var result = await model.OnGetAsync(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
    }

    private static CustomerDownloadModel CustomerModel(
        IFinancialDocumentQueries queries,
        IFinancialDocumentPdfRenderer renderer,
        Guid customerId)
    {
        var model = new CustomerDownloadModel(
            new FinancialDocumentDownloadService(queries, renderer));
        model.PageContext = PageContext(
            new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, customerId.ToString()),
                    new Claim(ClaimTypes.Role, "Customer")
                ],
                "test")));
        return model;
    }

    private static AdminDownloadModel AdminModel(
        IFinancialDocumentQueries queries,
        IFinancialDocumentPdfRenderer renderer)
    {
        var model = new AdminDownloadModel(
            new FinancialDocumentDownloadService(queries, renderer));
        model.PageContext = PageContext(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, "Admin")],
                "test")));
        return model;
    }

    private static PageContext PageContext(ClaimsPrincipal principal) =>
        new()
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

    private sealed class StubQueries : IFinancialDocumentQueries
    {
        private readonly FinancialDocument _document;

        public StubQueries(FinancialDocument document) => _document = document;

        public Task<FinancialDocument?> FindByIdForCustomerAsync(
            Guid documentId,
            Guid customerUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<FinancialDocument?>(
                documentId == _document.DocumentId &&
                customerUserId == _document.Customer.CustomerUserId
                    ? _document
                    : null);

        public Task<FinancialDocument?> FindByIdForAdminAsync(
            Guid documentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<FinancialDocument?>(
                documentId == _document.DocumentId ? _document : null);
    }

    private sealed class StubRenderer : IFinancialDocumentPdfRenderer
    {
        public int Count { get; private set; }

        public FinancialDocumentPdfFile Render(FinancialDocument document)
        {
            Count++;
            return new FinancialDocumentPdfFile(
                [0x25, 0x50, 0x44, 0x46],
                "document.pdf");
        }
    }
}
