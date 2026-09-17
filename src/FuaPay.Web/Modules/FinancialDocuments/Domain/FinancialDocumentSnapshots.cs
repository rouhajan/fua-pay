namespace FuaPay.Web.Modules.FinancialDocuments.Domain;

public sealed record FinancialDocumentCustomerSnapshot
{
    public FinancialDocumentCustomerSnapshot(
        Guid customerUserId,
        string displayName,
        string? email)
    {
        if (customerUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zákazníka nesmí být prázdné.",
                nameof(customerUserId));
        }

        CustomerUserId = customerUserId;
        DisplayName = FinancialDocumentText.NormalizeRequired(
            displayName,
            nameof(displayName),
            FinancialDocumentText.CustomerDisplayNameMaxLength);
        Email = FinancialDocumentText.NormalizeOptional(
            email,
            nameof(email),
            FinancialDocumentText.CustomerEmailMaxLength);
    }

    public Guid CustomerUserId { get; }

    public string DisplayName { get; }

    public string? Email { get; }
}

public sealed record FinancialDocumentProviderSnapshot
{
    public FinancialDocumentProviderSnapshot(
        string provider,
        string? reference,
        string? orderNumber)
    {
        Provider = FinancialDocumentText.NormalizeRequired(
            provider,
            nameof(provider),
            FinancialDocumentText.ProviderMaxLength);
        Reference = FinancialDocumentText.NormalizeOptional(
            reference,
            nameof(reference),
            FinancialDocumentText.ProviderReferenceMaxLength);
        OrderNumber = FinancialDocumentText.NormalizeOptional(
            orderNumber,
            nameof(orderNumber),
            FinancialDocumentText.ProviderOrderNumberMaxLength);
    }

    public string Provider { get; }

    public string? Reference { get; }

    public string? OrderNumber { get; }
}

public sealed record FinancialDocumentIssuerSnapshot
{
    public FinancialDocumentIssuerSnapshot(
        string legalName,
        string unitName,
        string addressLine1,
        string addressLine2,
        string country,
        string registrationNumber,
        string vatNumber,
        string contactEmail)
    {
        LegalName = FinancialDocumentText.NormalizeRequired(
            legalName,
            nameof(legalName));
        UnitName = FinancialDocumentText.NormalizeRequired(
            unitName,
            nameof(unitName));
        AddressLine1 = FinancialDocumentText.NormalizeRequired(
            addressLine1,
            nameof(addressLine1));
        AddressLine2 = FinancialDocumentText.NormalizeRequired(
            addressLine2,
            nameof(addressLine2));
        Country = FinancialDocumentText.NormalizeRequired(
            country,
            nameof(country));
        RegistrationNumber = FinancialDocumentText.NormalizeRequired(
            registrationNumber,
            nameof(registrationNumber));
        VatNumber = FinancialDocumentText.NormalizeRequired(
            vatNumber,
            nameof(vatNumber));
        ContactEmail = FinancialDocumentText.NormalizeRequired(
            contactEmail,
            nameof(contactEmail));
    }

    public string LegalName { get; }

    public string UnitName { get; }

    public string AddressLine1 { get; }

    public string AddressLine2 { get; }

    public string Country { get; }

    public string RegistrationNumber { get; }

    public string VatNumber { get; }

    public string ContactEmail { get; }
}

public sealed record FinancialDocumentTaxSnapshot
{
    public FinancialDocumentTaxSnapshot(
        FinancialDocumentTaxTreatment treatment,
        int vatRateBasisPoints,
        long taxBaseMinorUnits,
        long vatAmountMinorUnits)
    {
        if (
            treatment == FinancialDocumentTaxTreatment.Unknown ||
            !Enum.IsDefined(treatment))
        {
            throw new ArgumentOutOfRangeException(nameof(treatment));
        }

        if (vatRateBasisPoints is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(vatRateBasisPoints));
        }

        if (taxBaseMinorUnits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taxBaseMinorUnits));
        }

        if (vatAmountMinorUnits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vatAmountMinorUnits));
        }

        Treatment = treatment;
        VatRateBasisPoints = vatRateBasisPoints;
        TaxBaseMinorUnits = taxBaseMinorUnits;
        VatAmountMinorUnits = vatAmountMinorUnits;
    }

    public FinancialDocumentTaxTreatment Treatment { get; }

    public int VatRateBasisPoints { get; }

    public long TaxBaseMinorUnits { get; }

    public long VatAmountMinorUnits { get; }
}

public sealed record FinancialDocumentJobSnapshot
{
    public FinancialDocumentJobSnapshot(
        Guid jobId,
        string jobNumber,
        string title,
        string description,
        string serviceUnitName)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zakázky nesmí být prázdné.",
                nameof(jobId));
        }

        JobId = jobId;
        JobNumber = FinancialDocumentText.NormalizeRequired(
            jobNumber,
            nameof(jobNumber),
            FinancialDocumentText.JobNumberMaxLength);
        Title = FinancialDocumentText.NormalizeRequired(
            title,
            nameof(title),
            FinancialDocumentText.JobTitleMaxLength);
        Description = FinancialDocumentText.NormalizeRequired(
            description,
            nameof(description),
            FinancialDocumentText.JobDescriptionMaxLength);
        ServiceUnitName = FinancialDocumentText.NormalizeRequired(
            serviceUnitName,
            nameof(serviceUnitName),
            FinancialDocumentText.ServiceUnitNameMaxLength);
    }

    public Guid JobId { get; }

    public string JobNumber { get; }

    public string Title { get; }

    public string Description { get; }

    public string ServiceUnitName { get; }
}

internal static class FinancialDocumentText
{
    internal const int CustomerDisplayNameMaxLength = 256;
    internal const int CustomerEmailMaxLength = 320;
    internal const int CurrencyMaxLength = 3;
    internal const int ProviderMaxLength = 64;
    internal const int ProviderReferenceMaxLength = 160;
    internal const int ProviderOrderNumberMaxLength = 64;
    internal const int JobNumberMaxLength = 20;
    internal const int JobTitleMaxLength = 200;
    internal const int JobDescriptionMaxLength = 4_000;
    internal const int ServiceUnitNameMaxLength = 128;

    internal static string NormalizeRequired(
        string value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Textová hodnota nesmí být prázdná.",
                parameterName);
        }

        var normalized = value.Trim();

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Textová hodnota může mít nejvýše {maximumLength} znaků.",
                parameterName);
        }

        return normalized;
    }

    internal static string NormalizeRequired(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Textová hodnota nesmí být prázdná.",
                parameterName);
        }

        return value.Trim();
    }

    internal static string? NormalizeOptional(
        string? value,
        string parameterName,
        int maximumLength)
    {
        if (value is null)
        {
            return null;
        }

        return NormalizeRequired(value, parameterName, maximumLength);
    }
}
