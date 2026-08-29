using FuaPay.Web.Modules.Jobs.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed class CreditJobSettlementReturnNotAllowedException :
    InvalidOperationException
{
    public CreditJobSettlementReturnNotAllowedException(
        Guid jobId,
        JobPaymentStatus paymentStatus,
        JobSettlementType? settlementType)
        : base(
            $"Job '{jobId}' is not eligible for a credit settlement " +
            "return.")
    {
        JobId = jobId;
        PaymentStatus = paymentStatus;
        SettlementType = settlementType;
    }

    public Guid JobId { get; }

    public JobPaymentStatus PaymentStatus { get; }

    public JobSettlementType? SettlementType { get; }
}

public sealed class CreditJobSettlementHistoryInconsistentException :
    InvalidOperationException
{
    public CreditJobSettlementHistoryInconsistentException(
        Guid jobId,
        string reason)
        : base(
            $"Credit settlement history for job '{jobId}' is " +
            $"inconsistent: {reason}")
    {
        JobId = jobId;
    }

    public Guid JobId { get; }
}

public sealed class CreditJobSettlementReturnEffectInconsistentException :
    InvalidOperationException
{
    public CreditJobSettlementReturnEffectInconsistentException(
        Guid settlementReturnId,
        string reason)
        : base(
            $"Financial effect for settlement return " +
            $"'{settlementReturnId}' is inconsistent: {reason}")
    {
        SettlementReturnId = settlementReturnId;
    }

    public Guid SettlementReturnId { get; }
}
