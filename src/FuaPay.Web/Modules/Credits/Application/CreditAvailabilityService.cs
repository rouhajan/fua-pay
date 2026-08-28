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
        return GetAvailableAsync(
            account,
            Money.Zero,
            cancellationToken);
    }

    public Task<Money> GetAvailableExcludingAsync(
        CreditAccount account,
        Money existingBlockingAmount,
        CancellationToken cancellationToken = default)
    {
        if (existingBlockingAmount.MinorUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(existingBlockingAmount),
                "Excluded blocking amount must be positive.");
        }

        return GetAvailableAsync(
            account,
            existingBlockingAmount,
            cancellationToken);
    }

    private async Task<Money> GetAvailableAsync(
        CreditAccount account,
        Money excludedBlockingAmount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        var totalBlocking =
            await _repository.GetTotalBlockingAmountAsync(
                account.Id,
                cancellationToken);

        if (totalBlocking.MinorUnits < 0)
        {
            throw new InvalidDataException(
                $"Credit account '{account.Id}' has a negative blocking amount.");
        }

        if (
            totalBlocking.MinorUnits <
            excludedBlockingAmount.MinorUnits)
        {
            throw new InvalidDataException(
                $"Credit account '{account.Id}' blocking amount does not " +
                "contain the excluded existing block.");
        }

        var otherBlocking = totalBlocking.Subtract(
            excludedBlockingAmount);

        return account.Balance.Subtract(otherBlocking);
    }
}
