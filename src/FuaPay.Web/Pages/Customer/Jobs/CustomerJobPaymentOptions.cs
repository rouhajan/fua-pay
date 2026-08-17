namespace FuaPay.Web.Pages.Customer.Jobs;

public sealed record CustomerJobPaymentOptions
{
    public CustomerJobPaymentOptions(
        long priceMinorUnits,
        long creditBalanceMinorUnits)
    {
        if (priceMinorUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(priceMinorUnits));
        }

        if (creditBalanceMinorUnits < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(creditBalanceMinorUnits));
        }

        PriceMinorUnits = priceMinorUnits;
        CreditBalanceMinorUnits = creditBalanceMinorUnits;
    }

    public long PriceMinorUnits { get; }

    public long CreditBalanceMinorUnits { get; }

    public bool HasSufficientCredit =>
        CreditBalanceMinorUnits >= PriceMinorUnits;

    public long BalanceAfterPaymentMinorUnits =>
        HasSufficientCredit
            ? CreditBalanceMinorUnits - PriceMinorUnits
            : CreditBalanceMinorUnits;

    public long MissingCreditMinorUnits =>
        HasSufficientCredit
            ? 0
            : PriceMinorUnits - CreditBalanceMinorUnits;
}
