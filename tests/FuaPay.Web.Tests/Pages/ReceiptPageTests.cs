using System.Security.Claims;

using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Receipts.Application;
using FuaPay.Web.Pages.Customer.Jobs;
using FuaPay.Web.Tests.Modules.Receipts.Application;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Tests.Pages;

public sealed class ReceiptPageTests
{
    [Fact]
    public async Task OnGetAsync_OtherCustomerCannotDownloadReceipt()
    {
        var fixture = JobPaymentReceiptServiceTests.ReceiptFixture.Create(
            JobSettlementType.Credit);
        var renderer = new StubReceiptPdfRenderer();
        var model = CreateModel(
            fixture.Service,
            renderer,
            Guid.NewGuid());

        var result = await model.OnGetAsync(fixture.JobId);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(0, renderer.RenderCount);
    }

    [Fact]
    public async Task OnGetAsync_MissingJobReturnsNotFound()
    {
        var fixture = JobPaymentReceiptServiceTests.ReceiptFixture.Create(
            JobSettlementType.Credit);
        var renderer = new StubReceiptPdfRenderer();
        var model = CreateModel(
            fixture.Service,
            renderer,
            fixture.CustomerUserId);

        var result = await model.OnGetAsync(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(0, renderer.RenderCount);
    }

    [Fact]
    public async Task OnGetAsync_OwnPaidJobReturnsPrivatePdf()
    {
        var fixture = JobPaymentReceiptServiceTests.ReceiptFixture.Create(
            JobSettlementType.Credit);
        var renderer = new StubReceiptPdfRenderer();
        var model = CreateModel(
            fixture.Service,
            renderer,
            fixture.CustomerUserId);

        var result = await model.OnGetAsync(fixture.JobId);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(
            "fua-pay-potvrzeni-uhrady-test.pdf",
            file.FileDownloadName);
        Assert.Equal(
            new byte[] { 0x25, 0x50, 0x44, 0x46 },
            file.FileContents);
        Assert.Equal(
            "private, no-store",
            model.Response.Headers["Cache-Control"].ToString());
        Assert.Equal(
            "no-cache",
            model.Response.Headers["Pragma"].ToString());
        Assert.Equal(1, renderer.RenderCount);
        Assert.Equal(fixture.JobId, renderer.LastReceipt?.JobId);
    }

    private static ReceiptModel CreateModel(
        JobPaymentReceiptService service,
        IReceiptPdfRenderer renderer,
        Guid customerUserId)
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        customerUserId.ToString()),
                    new Claim(ClaimTypes.Role, "Customer")
                ],
                authenticationType: "test"));

        return new ReceiptModel(service, renderer)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = principal
                }
            }
        };
    }

    private sealed class StubReceiptPdfRenderer : IReceiptPdfRenderer
    {
        public int RenderCount { get; private set; }
        public JobPaymentReceiptData? LastReceipt { get; private set; }

        public ReceiptPdfFile Render(JobPaymentReceiptData receipt)
        {
            RenderCount++;
            LastReceipt = receipt;

            return new ReceiptPdfFile(
                [0x25, 0x50, 0x44, 0x46],
                "fua-pay-potvrzeni-uhrady-test.pdf");
        }
    }
}
