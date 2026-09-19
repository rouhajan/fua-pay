namespace FuaPay.Web.Modules.Credits.Application;

public interface ILegacySafeQCreditTransferRepository
{
    Task<PersistedLegacySafeQCreditTransfer?> FindByCommandIdAsync(
        Guid commandId,
        CancellationToken cancellationToken = default);

    Task<PersistedLegacySafeQCreditTransfer?> FindBySafeQUserIdAsync(
        string safeQUserId,
        CancellationToken cancellationToken = default);

    void Stage(
        LegacySafeQCreditTransferCommand command,
        DateTimeOffset acceptedAt);
}
