namespace FuaPay.Web.Modules.Receipts.Application;

public sealed class ReceiptConsistencyException : InvalidOperationException
{
    public ReceiptConsistencyException(string message)
        : base(message)
    {
    }
}
