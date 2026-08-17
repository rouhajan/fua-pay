using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed record VerifiedPaymentConfirmation
{
    public VerifiedPaymentConfirmation(
        PaymentProvider provider,
        string providerReference,
        Money amount)
    {
        if (
            provider == PaymentProvider.Unknown ||
            !Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(
                nameof(provider),
                "Poskytovatel platby není podporovaný.");
        }

        if (amount.MinorUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Potvrzená částka musí být kladná.");
        }

        Provider = provider;
        ProviderReference =
            PaymentProviderReference.Normalize(
                providerReference,
                nameof(providerReference));
        Amount = amount;
    }

    public PaymentProvider Provider { get; }

    public string ProviderReference { get; }

    public Money Amount { get; }
}
