using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;

namespace FuaPay.Web.Tests.Modules.FinancialDocuments.Application;

public sealed class ApprovedFinancialDocumentIssuanceProfileTests
{
    [Fact]
    public void ProfileCreatesExactApprovedImmutableSnapshots()
    {
        var profile = new ApprovedFinancialDocumentIssuanceProfile();

        var issuer = profile.CreateIssuerSnapshot();
        var tax = profile.CreateTaxSnapshot(12_100);

        Assert.Equal("Technická univerzita v Liberci", issuer.LegalName);
        Assert.Equal("Fakulta umění a architektury", issuer.UnitName);
        Assert.Equal("Studentská 1402/2", issuer.AddressLine1);
        Assert.Equal("461 17 Liberec 1", issuer.AddressLine2);
        Assert.Equal("Česká republika", issuer.Country);
        Assert.Equal("46747885", issuer.RegistrationNumber);
        Assert.Equal("CZ46747885", issuer.VatNumber);
        Assert.Equal("fua@tul.cz", issuer.ContactEmail);
        Assert.Equal(
            FinancialDocumentTaxTreatment.StandardRateIncluded,
            tax.Treatment);
        Assert.Equal(2_100, tax.VatRateBasisPoints);
        Assert.Equal(10_000, tax.TaxBaseMinorUnits);
        Assert.Equal(2_100, tax.VatAmountMinorUnits);
    }
}
