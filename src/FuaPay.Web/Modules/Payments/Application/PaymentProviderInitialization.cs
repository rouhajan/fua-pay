using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed record PaymentProviderInitializationRequest
{
    public PaymentProviderInitializationRequest(
        Guid paymentId,
        PaymentProvider provider,
        long orderNumber,
        Guid correlationId,
        PaymentPurposeType purposeType,
        Guid? jobId,
        Money amount)
    {
        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID platby nesmí být prázdné.",
                nameof(paymentId));
        }

        if (
            provider == PaymentProvider.Unknown ||
            !Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        if (
            orderNumber < 1 ||
            orderNumber > PaymentInitiation.MaximumOrderNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(orderNumber));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Korelační ID nesmí být prázdné.",
                nameof(correlationId));
        }

        if (
            purposeType == PaymentPurposeType.Unknown ||
            !Enum.IsDefined(purposeType))
        {
            throw new ArgumentOutOfRangeException(nameof(purposeType));
        }

        if (
            (purposeType == PaymentPurposeType.Job &&
             (!jobId.HasValue || jobId.Value == Guid.Empty)) ||
            (purposeType == PaymentPurposeType.CreditTopUp &&
             jobId.HasValue))
        {
            throw new ArgumentException(
                "Payment purpose and job binding are inconsistent.",
                nameof(jobId));
        }

        PaymentId = paymentId;
        Provider = provider;
        OrderNumber = orderNumber;
        CorrelationId = correlationId;
        PurposeType = purposeType;
        JobId = jobId;
        Amount = amount;
    }

    public Guid PaymentId { get; }

    public PaymentProvider Provider { get; }

    public long OrderNumber { get; }

    public Guid CorrelationId { get; }

    public string CorrelationData =>
        PaymentProviderCorrelation.Encode(
            PaymentId,
            CorrelationId);

    public PaymentPurposeType PurposeType { get; }

    public Guid? JobId { get; }

    public Money Amount { get; }
}

public sealed record PaymentProviderInitializationResult
{
    public PaymentProviderInitializationResult(
        PaymentProvider provider,
        string providerReference,
        Uri? processUri)
    {
        if (
            provider == PaymentProvider.Unknown ||
            !Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }

        Provider = provider;
        ProviderReference = PaymentProviderReference.Normalize(
            providerReference,
            nameof(providerReference));

        if (
            processUri is not null &&
            (!processUri.IsAbsoluteUri ||
             processUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "Process URI poskytovatele musí být absolutní HTTPS adresa.",
                nameof(processUri));
        }

        ProcessUri = processUri;
    }

    public PaymentProvider Provider { get; }

    public string ProviderReference { get; }

    public Uri? ProcessUri { get; }
}

public sealed record PaymentInitializationOutcome(
    Payment Payment,
    Uri? ProcessUri);

public sealed class PaymentProviderInitializationUncertainException :
    Exception
{
    public PaymentProviderInitializationUncertainException(
        PaymentProviderInitializationResult observedResult,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(observedResult);
        ObservedResult = observedResult;
    }

    public PaymentProviderInitializationResult ObservedResult { get; }
}
