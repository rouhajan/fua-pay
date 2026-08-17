namespace FuaPay.Web.BuildingBlocks.Domain;

public readonly record struct Money(long MinorUnits)
{
    public const string CurrencyCode = "CZK";

    public static Money Zero => new(0);

    public static Money FromCrowns(decimal crowns)
    {
        var minorUnits = checked(crowns * 100m);

        if (decimal.Truncate(minorUnits) != minorUnits)
        {
            throw new ArgumentException(
                "Částka nesmí mít více než dvě desetinná místa.",
                nameof(crowns));
        }

        return new Money(checked((long)minorUnits));
    }

    public decimal ToCrowns()
    {
        return MinorUnits / 100m;
    }

    public Money Add(Money other)
    {
        return new Money(checked(MinorUnits + other.MinorUnits));
    }

    public Money Subtract(Money other)
    {
        return new Money(checked(MinorUnits - other.MinorUnits));
    }

    public Money Negate()
    {
        return new Money(checked(-MinorUnits));
    }
}
