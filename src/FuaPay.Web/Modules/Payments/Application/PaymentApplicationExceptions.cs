using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed class PaymentNotFoundException : InvalidOperationException
{
    public PaymentNotFoundException(Guid paymentId)
        : base($"Platba '{paymentId}' nebyla nalezena.")
    {
        PaymentId = paymentId;
    }

    public Guid PaymentId { get; }
}

public sealed class PaymentAccessDeniedException : InvalidOperationException
{
    public PaymentAccessDeniedException(Guid paymentId, Guid userId)
        : base($"Uživatel '{userId}' nemá přístup k platbě '{paymentId}'.")
    {
    }
}

public sealed class PaymentConcurrencyException : InvalidOperationException
{
    public PaymentConcurrencyException(
        Guid paymentId,
        Exception? innerException = null)
        : base($"Platba '{paymentId}' byla souběžně změněna.", innerException)
    {
    }
}

public sealed class PaymentCreationRequestAlreadyExistsException :
    InvalidOperationException
{
    public PaymentCreationRequestAlreadyExistsException(
        Guid creationRequestId,
        Exception? innerException = null)
        : base(
            $"Creation request platby '{creationRequestId}' již existuje.",
            innerException)
    {
        CreationRequestId = creationRequestId;
    }

    public Guid CreationRequestId { get; }
}

public sealed class PaymentCreationRequestConflictException :
    InvalidOperationException
{
    public PaymentCreationRequestConflictException(Guid creationRequestId)
        : base(
            $"Creation request platby '{creationRequestId}' byl již použit s jinými daty.")
    {
        CreationRequestId = creationRequestId;
    }

    public Guid CreationRequestId { get; }
}

public sealed class PaymentAmountNotAllowedException :
    InvalidOperationException
{
    public PaymentAmountNotAllowedException()
        : base("Částka platby je mimo povolený rozsah.")
    {
    }
}

public sealed class PaymentProviderUnavailableException :
    InvalidOperationException
{
    public PaymentProviderUnavailableException()
        : base("Platební poskytovatel není v tomto prostředí dostupný.")
    {
    }
}

public sealed class BlockingJobPaymentAlreadyExistsException :
    InvalidOperationException
{
    public BlockingJobPaymentAlreadyExistsException(Guid jobId)
        : base($"Pro zakázku '{jobId}' již existuje otevřená platba.")
    {
    }
}

public sealed class PaymentProviderReferenceNotFoundException :
    InvalidOperationException
{
    public PaymentProviderReferenceNotFoundException(
        PaymentProvider provider,
        string providerReference)
        : base(
            $"Platba poskytovatele '{provider}' nebyla nalezena.")
    {
        Provider = provider;
        ProviderReference = providerReference;
    }

    public PaymentProvider Provider { get; }

    public string ProviderReference { get; }
}

public sealed class PaymentConfirmationMismatchException :
    InvalidOperationException
{
    public PaymentConfirmationMismatchException(Guid paymentId)
        : base(
            $"Ověřené potvrzení neodpovídá platbě '{paymentId}'.")
    {
        PaymentId = paymentId;
    }

    public Guid PaymentId { get; }
}

public sealed class DevelopmentPaymentProviderMismatchException :
    InvalidOperationException
{
    public DevelopmentPaymentProviderMismatchException(
        Guid paymentId,
        PaymentProvider provider)
        : base(
            $"Platbu '{paymentId}' poskytovatele '{provider}' " +
            "nelze měnit vývojovou simulací.")
    {
        PaymentId = paymentId;
        Provider = provider;
    }

    public Guid PaymentId { get; }

    public PaymentProvider Provider { get; }
}
