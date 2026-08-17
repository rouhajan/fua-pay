namespace FuaPay.Web.Modules.Credits.Application;

public interface ICreditAdjustmentCommandRepository
{
    Task<PersistedCreditAdjustmentCommand?> FindAsync(
        Guid commandId,
        CancellationToken cancellationToken = default);

    void Stage(
        CreditAdjustmentCommand command,
        DateTimeOffset acceptedAt);
}
