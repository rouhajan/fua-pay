using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed class CreditService
{
    private readonly ICreditAccountRepository _repository;
    private readonly TimeProvider _timeProvider;

    public CreditService(
        ICreditAccountRepository repository,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<CreditMovement> CreditAsync(
        Guid ownerId,
        Guid operationId,
        Money amount,
        string description,
        CancellationToken cancellationToken = default)
    {
        ValidateOwnerId(ownerId);

        var account = await _repository.FindByOwnerIdAsync(
            ownerId,
            cancellationToken);

        var isNewAccount = account is null;

        account ??= new CreditAccount(
            Guid.NewGuid(),
            ownerId);

        var movement = account.Credit(
            operationId,
            amount,
            _timeProvider.GetUtcNow(),
            description);

        if (isNewAccount)
        {
            await _repository.AddAsync(
                account,
                cancellationToken);
        }
        else
        {
            await _repository.SaveAsync(
                account,
                cancellationToken);
        }

        return movement;
    }

    public async Task<CreditMovement> DebitAsync(
        Guid ownerId,
        Guid operationId,
        Money amount,
        string description,
        CancellationToken cancellationToken = default)
    {
        ValidateOwnerId(ownerId);

        var account = await _repository.FindByOwnerIdAsync(
            ownerId,
            cancellationToken);

        if (account is null)
        {
            throw new CreditAccountNotFoundException(ownerId);
        }

        var movement = account.Debit(
            operationId,
            amount,
            _timeProvider.GetUtcNow(),
            description);

        await _repository.SaveAsync(
            account,
            cancellationToken);

        return movement;
    }

    private static void ValidateOwnerId(Guid ownerId)
    {
        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID vlastníka nesmí být prázdné.",
                nameof(ownerId));
        }
    }
}
