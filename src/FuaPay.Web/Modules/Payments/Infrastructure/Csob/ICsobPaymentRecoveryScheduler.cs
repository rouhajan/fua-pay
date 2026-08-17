namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

public interface ICsobPaymentRecoveryScheduler
{
    Task<Guid> ScheduleReturnAsync(
        string providerReference,
        CancellationToken cancellationToken = default);
}
