using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed class CreditAvailabilityService
{
    private readonly ICreditAvailabilityRepository _repository;

    public CreditAvailabilityService(
        ICreditAvailabilityRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public Task<Money> GetAvailableAsync(
        CreditAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        return GetAvailableAsync(
            account.Id,
            account.Balance,
            Money.Zero,
            cancellationToken);
    }

    public Task<Money> GetAvailableAsync(
        CreditAccountSummary account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        return GetAvailableAsync(
            account.Id,
            new Money(account.BalanceMinorUnits),
            Money.Zero,
            cancellationToken);
    }

    public Task<Money> GetAvailableExcludingAsync(
        CreditAccount account,
        Money existingBlockingAmount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (existingBlockingAmount.MinorUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(existingBlockingAmount),
                "Excluded blocking amount must be positive.");
        }

        return GetAvailableAsync(
            account.Id,
            account.Balance,
            existingBlockingAmount,
            cancellationToken);
    }

    private async Task<Money> GetAvailableAsync(
        Guid accountId,
        Money balance,
        Money excludedBlockingAmount,
        CancellationToken cancellationToken)
    {
        var totalBlocking =
            await _repository.GetTotalBlockingAmountAsync(
                accountId,
                cancellationToken);

        if (totalBlocking.MinorUnits < 0)
        {
            throw new InvalidDataException(
                $"Credit account '{accountId}' has a negative blocking amount.");
        }

        if (
            totalBlocking.MinorUnits <
            excludedBlockingAmount.MinorUnits)
        {
            throw new InvalidDataException(
                $"Credit account '{accountId}' blocking amount does not " +
                "contain the excluded existing block.");
        }

        var otherBlocking = totalBlocking.Subtract(
            excludedBlockingAmount);

        return balance.Subtract(otherBlocking);
    }
}
