namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public interface ICsobPaymentRecoveryScheduler
{
    Task<Guid> ScheduleReturnAsync(
        CsobVerifiedPaymentReturn verifiedReturn,
        CancellationToken cancellationToken = default);
}
