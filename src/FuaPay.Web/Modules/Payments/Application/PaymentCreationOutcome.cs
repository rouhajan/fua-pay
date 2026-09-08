using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public enum PaymentCreationDisposition
{
    FreshInitialization = 1,
    ResumedInitialization = 2,
    ExistingPayment = 3
}

public sealed record PaymentCreationOutcome
{
    internal PaymentCreationOutcome(
        Payment payment,
        Uri? processUri,
        PaymentCreationDisposition disposition)
    {
        ArgumentNullException.ThrowIfNull(payment);

        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }

        if (
            processUri is not null &&
            (!processUri.IsAbsoluteUri ||
             processUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "Trusted process URI must be an absolute HTTPS URI.",
                nameof(processUri));
        }

        Payment = payment;
        ProcessUri = processUri;
        Disposition = disposition;
    }

    public Payment Payment { get; }

    public Uri? ProcessUri { get; }

    public PaymentCreationDisposition Disposition { get; }

    public bool ShouldRedirectToProvider =>
        ProcessUri is not null &&
        Disposition is
            PaymentCreationDisposition.FreshInitialization or
            PaymentCreationDisposition.ResumedInitialization;
}
