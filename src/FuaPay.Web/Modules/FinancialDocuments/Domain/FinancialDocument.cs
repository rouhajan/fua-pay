using System.Text.RegularExpressions;

namespace FuaPay.Web.Modules.FinancialDocuments.Domain;

public sealed partial class FinancialDocument
{
    public const int CurrentSchemaVersion = 2;
    public const int CurrentRenderVersion = 2;

    public FinancialDocument(
        Guid documentId,
        string documentNumber,
        FinancialDocumentType documentType,
        FinancialDocumentSourceType sourceType,
        Guid sourceId,
        FinancialDocumentCustomerSnapshot customer,
        long amountMinorUnits,
        string currency,
        DateTimeOffset financialEventAt,
        DateTimeOffset issuedAt,
        FinancialDocumentSettlementMethod settlementMethod,
        FinancialDocumentIssuerSnapshot? issuer,
        FinancialDocumentTaxSnapshot? tax,
        FinancialDocumentProviderSnapshot? provider,
        FinancialDocumentJobSnapshot? job,
        int schemaVersion,
        int renderVersion)
    {
        ValidateId(documentId, nameof(documentId));
        ValidateDocumentType(documentType);
        ValidateSource(sourceType, sourceId);
        ArgumentNullException.ThrowIfNull(customer);
        ValidateAmount(amountMinorUnits);
        ValidateTimestamps(financialEventAt, issuedAt);
        ValidateDocumentShape(
            documentType,
            sourceType,
            settlementMethod,
            provider,
            job);
        ValidateVersionPair(schemaVersion, renderVersion);
        ValidateVersionedSnapshot(
            schemaVersion,
            renderVersion,
            amountMinorUnits,
            issuer,
            tax);

        var normalizedNumber = documentNumber?.Trim().ToUpperInvariant();

        if (
            normalizedNumber is null ||
            !DocumentNumberPattern().IsMatch(normalizedNumber))
        {
            throw new ArgumentException(
                "Číslo dokladu musí mít tvar FUA-YYYY-NNNNNN.",
                nameof(documentNumber));
        }

        var normalizedCurrency = currency?.Trim().ToUpperInvariant();

        if (
            normalizedCurrency is null ||
            !CurrencyPattern().IsMatch(normalizedCurrency))
        {
            throw new ArgumentException(
                "Měna musí být třípísmenný kód tvořený velkými písmeny.",
                nameof(currency));
        }

        DocumentId = documentId;
        DocumentNumber = normalizedNumber;
        DocumentType = documentType;
        SourceType = sourceType;
        SourceId = sourceId;
        Customer = customer;
        AmountMinorUnits = amountMinorUnits;
        Currency = normalizedCurrency;
        FinancialEventAt = financialEventAt;
        IssuedAt = issuedAt;
        SettlementMethod = settlementMethod;
        Issuer = issuer;
        Tax = tax;
        Provider = provider;
        Job = job;
        SchemaVersion = schemaVersion;
        RenderVersion = renderVersion;
    }

    public Guid DocumentId { get; }

    public string DocumentNumber { get; }

    public FinancialDocumentType DocumentType { get; }

    public FinancialDocumentSourceType SourceType { get; }

    public Guid SourceId { get; }

    public FinancialDocumentCustomerSnapshot Customer { get; }

    public long AmountMinorUnits { get; }

    public string Currency { get; }

    public DateTimeOffset FinancialEventAt { get; }

    public DateTimeOffset IssuedAt { get; }

    public FinancialDocumentSettlementMethod SettlementMethod { get; }

    public FinancialDocumentIssuerSnapshot? Issuer { get; }

    public FinancialDocumentTaxSnapshot? Tax { get; }

    public FinancialDocumentProviderSnapshot? Provider { get; }

    public FinancialDocumentJobSnapshot? Job { get; }

    public int SchemaVersion { get; }

    public int RenderVersion { get; }

    public static FinancialDocument CreateManualCreditTopUp(
        Guid documentId,
        string documentNumber,
        Guid commandId,
        FinancialDocumentCustomerSnapshot customer,
        long amountMinorUnits,
        string currency,
        DateTimeOffset financialEventAt,
        DateTimeOffset issuedAt,
        FinancialDocumentIssuerSnapshot issuer,
        FinancialDocumentTaxSnapshot tax)
    {
        return new FinancialDocument(
            documentId,
            documentNumber,
            FinancialDocumentType.ManualCreditTopUp,
            FinancialDocumentSourceType.ManualCreditTopUp,
            commandId,
            customer,
            amountMinorUnits,
            currency,
            financialEventAt,
            issuedAt,
            FinancialDocumentSettlementMethod.ManualCreditTopUp,
            issuer,
            tax,
            null,
            null,
            CurrentSchemaVersion,
            CurrentRenderVersion);
    }

    public static FinancialDocument CreateCardWalletTopUp(
        Guid documentId,
        string documentNumber,
        Guid paymentId,
        FinancialDocumentCustomerSnapshot customer,
        long amountMinorUnits,
        string currency,
        DateTimeOffset financialEventAt,
        DateTimeOffset issuedAt,
        FinancialDocumentIssuerSnapshot issuer,
        FinancialDocumentTaxSnapshot tax,
        FinancialDocumentProviderSnapshot provider)
    {
        return new FinancialDocument(
            documentId,
            documentNumber,
            FinancialDocumentType.CardWalletTopUp,
            FinancialDocumentSourceType.Payment,
            paymentId,
            customer,
            amountMinorUnits,
            currency,
            financialEventAt,
            issuedAt,
            FinancialDocumentSettlementMethod.PaymentProvider,
            issuer,
            tax,
            provider,
            null,
            CurrentSchemaVersion,
            CurrentRenderVersion);
    }

    public static FinancialDocument CreateDirectJobCardPayment(
        Guid documentId,
        string documentNumber,
        Guid paymentId,
        FinancialDocumentCustomerSnapshot customer,
        long amountMinorUnits,
        string currency,
        DateTimeOffset financialEventAt,
        DateTimeOffset issuedAt,
        FinancialDocumentIssuerSnapshot issuer,
        FinancialDocumentTaxSnapshot tax,
        FinancialDocumentProviderSnapshot provider,
        FinancialDocumentJobSnapshot job)
    {
        return new FinancialDocument(
            documentId,
            documentNumber,
            FinancialDocumentType.DirectJobCardPayment,
            FinancialDocumentSourceType.Payment,
            paymentId,
            customer,
            amountMinorUnits,
            currency,
            financialEventAt,
            issuedAt,
            FinancialDocumentSettlementMethod.PaymentProvider,
            issuer,
            tax,
            provider,
            job,
            CurrentSchemaVersion,
            CurrentRenderVersion);
    }

    private static void ValidateVersionedSnapshot(
        int schemaVersion,
        int renderVersion,
        long grossMinorUnits,
        FinancialDocumentIssuerSnapshot? issuer,
        FinancialDocumentTaxSnapshot? tax)
    {
        if (schemaVersion == 1 && renderVersion == 1)
        {
            if (tax is not null)
            {
                throw new ArgumentException(
                    "Schema 1 documents cannot contain a tax snapshot.",
                    nameof(tax));
            }

            return;
        }

        if (schemaVersion != 2 || renderVersion != 2)
        {
            return;
        }

        if (issuer is null)
        {
            throw new ArgumentException(
                "Schema 2 documents require an issuer snapshot.",
                nameof(issuer));
        }

        if (tax is null)
        {
            throw new ArgumentException(
                "Schema 2 documents require a tax snapshot.",
                nameof(tax));
        }

        if (!FinancialDocumentTaxPolicy.IsApprovedSnapshot(
                grossMinorUnits,
                tax))
        {
            throw new ArgumentException(
                "Schema 2 documents require the approved tax treatment, rate and gross-inclusive breakdown.",
                nameof(tax));
        }
    }

    private static void ValidateSource(
        FinancialDocumentSourceType sourceType,
        Guid sourceId)
    {
        if (
            sourceType == FinancialDocumentSourceType.Unknown ||
            !Enum.IsDefined(sourceType))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceType));
        }

        ValidateId(sourceId, nameof(sourceId));
    }

    private static void ValidateDocumentShape(
        FinancialDocumentType documentType,
        FinancialDocumentSourceType sourceType,
        FinancialDocumentSettlementMethod settlementMethod,
        FinancialDocumentProviderSnapshot? provider,
        FinancialDocumentJobSnapshot? job)
    {
        if (
            settlementMethod == FinancialDocumentSettlementMethod.Unknown ||
            !Enum.IsDefined(settlementMethod))
        {
            throw new ArgumentOutOfRangeException(nameof(settlementMethod));
        }

        var isValid = documentType switch
        {
            FinancialDocumentType.ManualCreditTopUp =>
                sourceType == FinancialDocumentSourceType.ManualCreditTopUp &&
                settlementMethod ==
                    FinancialDocumentSettlementMethod.ManualCreditTopUp &&
                provider is null &&
                job is null,

            FinancialDocumentType.CardWalletTopUp =>
                sourceType == FinancialDocumentSourceType.Payment &&
                settlementMethod ==
                    FinancialDocumentSettlementMethod.PaymentProvider &&
                provider is not null &&
                job is null,

            FinancialDocumentType.DirectJobCardPayment =>
                sourceType == FinancialDocumentSourceType.Payment &&
                settlementMethod ==
                    FinancialDocumentSettlementMethod.PaymentProvider &&
                provider is not null &&
                job is not null,

            _ => false
        };

        if (!isValid)
        {
            throw new ArgumentException(
                "Typ dokladu neodpovídá zdroji, způsobu vypořádání nebo snapshotům.",
                nameof(documentType));
        }
    }

    private static void ValidateDocumentType(
        FinancialDocumentType documentType)
    {
        if (
            documentType == FinancialDocumentType.Unknown ||
            !Enum.IsDefined(documentType))
        {
            throw new ArgumentOutOfRangeException(nameof(documentType));
        }
    }

    private static void ValidateAmount(long amountMinorUnits)
    {
        if (amountMinorUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountMinorUnits));
        }
    }

    private static void ValidateTimestamps(
        DateTimeOffset financialEventAt,
        DateTimeOffset issuedAt)
    {
        if (financialEventAt == default)
        {
            throw new ArgumentException(
                "Čas finanční události nesmí být prázdný.",
                nameof(financialEventAt));
        }

        if (issuedAt == default)
        {
            throw new ArgumentException(
                "Čas vystavení nesmí být prázdný.",
                nameof(issuedAt));
        }

        if (issuedAt < financialEventAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(issuedAt),
                "Čas vystavení nesmí předcházet finanční události.");
        }
    }

    private static void ValidateVersionPair(
        int schemaVersion,
        int renderVersion)
    {
        if (
            (schemaVersion, renderVersion) is not ((1, 1) or (2, 2)))
        {
            throw new ArgumentException(
                "Podporované jsou pouze dvojice schema/render 1/1 a 2/2.",
                nameof(schemaVersion));
        }
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "ID nesmí být prázdné.",
                parameterName);
        }
    }

    [GeneratedRegex(
        "^FUA-[0-9]{4}-[0-9]{6}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DocumentNumberPattern();

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();
}
