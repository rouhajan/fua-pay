using System.Security.Cryptography;

namespace FuaPay.Web.Modules.Payments.Domain;

public static class PaymentProviderCorrelation
{
    private const byte CurrentVersion = 1;
    private const int PayloadLength = 33;

    public static Guid CreateCorrelationId()
    {
        Span<byte> bytes = stackalloc byte[16];

        while (true)
        {
            RandomNumberGenerator.Fill(bytes);
            var correlationId = new Guid(bytes);

            if (correlationId != Guid.Empty)
            {
                return correlationId;
            }
        }
    }

    public static string Encode(
        Guid paymentId,
        Guid correlationId)
    {
        ValidateId(paymentId, nameof(paymentId));
        ValidateId(correlationId, nameof(correlationId));

        Span<byte> payload = stackalloc byte[PayloadLength];
        payload[0] = CurrentVersion;

        if (!paymentId.TryWriteBytes(payload[1..17]))
        {
            throw new InvalidOperationException(
                "ID platby se nepodařilo zapsat do korelačních dat.");
        }

        if (!correlationId.TryWriteBytes(payload[17..33]))
        {
            throw new InvalidOperationException(
                "Korelační ID se nepodařilo zapsat do korelačních dat.");
        }

        return Convert.ToBase64String(payload);
    }

    private static void ValidateId(
        Guid value,
        string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "ID nesmí být prázdné.",
                parameterName);
        }
    }
}
