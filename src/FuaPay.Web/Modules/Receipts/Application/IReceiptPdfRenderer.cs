namespace FuaPay.Web.Modules.Receipts.Application;

public interface IReceiptPdfRenderer
{
    ReceiptPdfFile Render(JobPaymentReceiptData receipt);
}
