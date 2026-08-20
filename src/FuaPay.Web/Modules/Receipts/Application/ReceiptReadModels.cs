namespace FuaPay.Web.Modules.Receipts.Application;

public sealed record JobPaymentReceiptData(
    string ReceiptReference,
    Guid JobId,
    string JobNumber,
    string JobTitle,
    string ServiceUnitCode,
    string ServiceUnitName,
    string CustomerName,
    string? CustomerEmail,
    long GrossAmountMinorUnits,
    long TaxBaseMinorUnits,
    long VatAmountMinorUnits,
    int VatRatePercent,
    DateTimeOffset SettledAt,
    string SettlementMethod,
    string? PaymentProvider,
    string? ProviderReference,
    Guid SettlementReferenceId,
    ReceiptIssuerConfiguration Issuer,
    bool PreviewMode);

public sealed record ReceiptPdfFile(
    byte[] Content,
    string FileName);
