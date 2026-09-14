namespace FuaPay.Web.Modules.Credits.Application;

public interface IManualCreditTopUpCommandRepository
{
    Task<PersistedManualCreditTopUpCommand?> FindAsync(
        Guid commandId,
        CancellationToken cancellationToken = default);

    void Stage(
        ManualCreditTopUpCommand command,
        DateTimeOffset acceptedAt);
}
