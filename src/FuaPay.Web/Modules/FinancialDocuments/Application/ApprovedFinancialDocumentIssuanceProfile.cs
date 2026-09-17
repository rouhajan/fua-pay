using FuaPay.Web.Modules.FinancialDocuments.Domain;

namespace FuaPay.Web.Modules.FinancialDocuments.Application;

internal sealed class ApprovedFinancialDocumentIssuanceProfile :
    IFinancialDocumentIssuanceProfile
{
    public FinancialDocumentIssuerSnapshot CreateIssuerSnapshot() =>
        new(
            "Technická univerzita v Liberci",
            "Fakulta umění a architektury",
            "Studentská 1402/2",
            "461 17 Liberec 1",
            "Česká republika",
            "46747885",
            "CZ46747885",
            "fua@tul.cz");

    public FinancialDocumentTaxSnapshot CreateTaxSnapshot(
        long grossMinorUnits) =>
        FinancialDocumentTaxPolicy.CreateApprovedSnapshot(grossMinorUnits);
}
