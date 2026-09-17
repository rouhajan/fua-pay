namespace FuaPay.Web.Modules.FinancialDocuments.Domain;

public static class FinancialDocumentTaxPolicy
{
    public const int ApprovedVatRateBasisPoints = 2_100;

    private const decimal BasisPointsPerWhole = 10_000m;

    public static FinancialDocumentTaxSnapshot CreateApprovedSnapshot(
        long grossMinorUnits)
    {
        var (baseMinorUnits, vatMinorUnits) = CalculateApprovedAmounts(
            grossMinorUnits);

        return new FinancialDocumentTaxSnapshot(
            FinancialDocumentTaxTreatment.StandardRateIncluded,
            ApprovedVatRateBasisPoints,
            baseMinorUnits,
            vatMinorUnits);
    }

    public static bool IsApprovedSnapshot(
        long grossMinorUnits,
        FinancialDocumentTaxSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (
            snapshot.Treatment !=
                FinancialDocumentTaxTreatment.StandardRateIncluded ||
            snapshot.VatRateBasisPoints != ApprovedVatRateBasisPoints)
        {
            return false;
        }

        var (expectedBase, expectedVat) = CalculateApprovedAmounts(
            grossMinorUnits);

        return
            snapshot.TaxBaseMinorUnits == expectedBase &&
            snapshot.VatAmountMinorUnits == expectedVat &&
            checked(
                snapshot.TaxBaseMinorUnits +
                snapshot.VatAmountMinorUnits) == grossMinorUnits;
    }

    private static (long BaseMinorUnits, long VatMinorUnits)
        CalculateApprovedAmounts(long grossMinorUnits)
    {
        if (grossMinorUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(grossMinorUnits));
        }

        var divisor = BasisPointsPerWhole + ApprovedVatRateBasisPoints;
        var exactBase = grossMinorUnits * BasisPointsPerWhole / divisor;
        var baseMinorUnits = checked((long)decimal.Round(
            exactBase,
            decimals: 0,
            MidpointRounding.AwayFromZero));
        var vatMinorUnits = checked(grossMinorUnits - baseMinorUnits);

        return (baseMinorUnits, vatMinorUnits);
    }
}
