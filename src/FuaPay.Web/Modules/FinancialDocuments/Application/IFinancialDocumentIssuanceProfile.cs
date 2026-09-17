using FuaPay.Web.Modules.FinancialDocuments.Domain;

namespace FuaPay.Web.Modules.FinancialDocuments.Application;

public interface IFinancialDocumentIssuanceProfile
{
    FinancialDocumentIssuerSnapshot CreateIssuerSnapshot();

    FinancialDocumentTaxSnapshot CreateTaxSnapshot(long grossMinorUnits);
}
