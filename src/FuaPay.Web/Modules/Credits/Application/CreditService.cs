using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed class CreditService
{
    private readonly ICreditAccountRepository _repository;
    private readonly IPrintReservationRepository _reservationRepository;
    private readonly IApplicationTransaction _transaction;
    private readonly TimeProvider _timeProvider;

    public CreditService(
        ICreditAccountRepository repository,
        IPrintReservationRepository reservationRepository,
        IApplicationTransaction transaction,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(reservationRepository);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _repository = repository;
        _reservationRepository = reservationRepository;
        _transaction = transaction;
        _timeProvider = timeProvider;
    }

    public Task<CreditMovement> CreditAsync(
        Guid ownerId,
        Guid operationId,
        Money amount,
        string description,
        CancellationToken cancellationToken = default)
    {
        ValidateOwnerId(ownerId);

        return _transaction.ExecuteAsync(
            ct => CreditInsideTransactionAsync(
                ownerId,
                operationId,
                amount,
                description,
                ct),
            cancellationToken);
    }

    public Task<CreditMovement> DebitAsync(
        Guid ownerId,
        Guid operationId,
        Money amount,
        string description,
        CancellationToken cancellationToken = default)
    {
        ValidateOwnerId(ownerId);

        return _transaction.ExecuteAsync(
            ct => DebitInsideTransactionAsync(
                ownerId,
                operationId,
                amount,
                description,
                ct),
            cancellationToken);
    }

    private async Task<CreditMovement> CreditInsideTransactionAsync(
        Guid ownerId,
        Guid operationId,
        Money amount,
        string description,
        CancellationToken cancellationToken)
    {
        var account = await _repository.FindByOwnerIdForUpdateAsync(
            ownerId,
            cancellationToken);

        if (account is null)
        {
            await _repository.LockOwnerForAccountCreationAsync(
                ownerId,
                cancellationToken);

            account = await _repository.FindByOwnerIdForUpdateAsync(
                ownerId,
                cancellationToken);
        }

        var isNewAccount = account is null;

        account ??= new CreditAccount(Guid.NewGuid(), ownerId);

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

    private async Task<CreditMovement> DebitInsideTransactionAsync(
        Guid ownerId,
        Guid operationId,
        Money amount,
        string description,
        CancellationToken cancellationToken)
    {
        var account = await _repository.FindByOwnerIdForUpdateAsync(
            ownerId,
            cancellationToken);

        if (account is null)
        {
            throw new CreditAccountNotFoundException(ownerId);
        }

        var blockingAmount =
            await _reservationRepository.GetBlockingAmountAsync(
                account.Id,
                cancellationToken);
        var available = account.Balance.Subtract(blockingAmount);

        var movement = account.Debit(
            operationId,
            amount,
            available,
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
