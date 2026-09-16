namespace FuaPay.Web.Modules.FinancialDocuments.Infrastructure.Persistence;

internal sealed class FinancialDocumentEntity
{
    public Guid DocumentId { get; set; }

    public string DocumentNumber { get; set; } = string.Empty;

    public int DocumentType { get; set; }

    public int SourceType { get; set; }

    public Guid SourceId { get; set; }

    public Guid CustomerUserId { get; set; }

    public string CustomerDisplayName { get; set; } = string.Empty;

    public string? CustomerEmail { get; set; }

    public long AmountMinorUnits { get; set; }

    public string Currency { get; set; } = string.Empty;

    public DateTimeOffset FinancialEventAt { get; set; }

    public DateTimeOffset IssuedAt { get; set; }

    public int SettlementMethod { get; set; }

    public string? IssuerLegalName { get; set; }

    public string? IssuerUnitName { get; set; }

    public string? IssuerAddressLine1 { get; set; }

    public string? IssuerAddressLine2 { get; set; }

    public string? IssuerCountry { get; set; }

    public string? IssuerRegistrationNumber { get; set; }

    public string? IssuerVatNumber { get; set; }

    public string? IssuerContactEmail { get; set; }

    public string? Provider { get; set; }

    public string? ProviderReference { get; set; }

    public string? ProviderOrderNumber { get; set; }

    public Guid? JobId { get; set; }

    public string? JobNumber { get; set; }

    public string? JobTitle { get; set; }

    public string? JobDescription { get; set; }

    public string? ServiceUnitName { get; set; }

    public int SchemaVersion { get; set; }

    public int RenderVersion { get; set; }
}
