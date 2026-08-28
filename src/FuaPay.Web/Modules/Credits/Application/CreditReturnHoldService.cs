using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed record CreateCreditReturnHoldCommand(
    Guid SettlementReturnId,
    Guid CreditOwnerId,
    Money Amount);

public sealed record CreditReturnHoldResult(
    CreditReturnHold Hold,
    bool Created);

public sealed class CreditReturnHoldService
{
    private readonly ICreditAccountRepository _accountRepository;
    private readonly ICreditReturnHoldRepository _holdRepository;
    private readonly CreditAvailabilityService _availabilityService;
    private readonly IApplicationTransaction _transaction;
    private readonly TimeProvider _timeProvider;

    public CreditReturnHoldService(
        ICreditAccountRepository accountRepository,
        ICreditReturnHoldRepository holdRepository,
        CreditAvailabilityService availabilityService,
        IApplicationTransaction transaction,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(accountRepository);
        ArgumentNullException.ThrowIfNull(holdRepository);
        ArgumentNullException.ThrowIfNull(availabilityService);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _accountRepository = accountRepository;
        _holdRepository = holdRepository;
        _availabilityService = availabilityService;
        _transaction = transaction;
        _timeProvider = timeProvider;
    }

    public async Task<CreditReturnHoldResult> CreateAsync(
        CreateCreditReturnHoldCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateId(
            command.SettlementReturnId,
            nameof(command.SettlementReturnId));
        ValidateId(
            command.CreditOwnerId,
            nameof(command.CreditOwnerId));

        if (command.Amount.MinorUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "Credit return hold amount must be positive.");
        }

        try
        {
            return await _transaction.ExecuteAsync(
                ct => CreateInsideTransactionAsync(command, ct),
                cancellationToken);
        }
        catch (CreditReturnHoldAlreadyExistsException)
        {
            return await _transaction.ExecuteAsync(
                ct => CreateInsideTransactionAsync(command, ct),
                cancellationToken);
        }
    }

    private async Task<CreditReturnHoldResult>
        CreateInsideTransactionAsync(
            CreateCreditReturnHoldCommand command,
            CancellationToken cancellationToken)
    {
        var account = await _accountRepository
            .FindByOwnerIdForUpdateAsync(
                command.CreditOwnerId,
                cancellationToken)
            ?? throw new CreditAccountNotFoundException(
                command.CreditOwnerId);

        var existing =
            await _holdRepository.FindBySettlementReturnIdAsync(
                command.SettlementReturnId,
                cancellationToken);

        if (existing is not null)
        {
            if (
                existing.CreditAccountId == account.Id &&
                existing.Amount == command.Amount)
            {
                return new CreditReturnHoldResult(
                    existing,
                    Created: false);
            }

            throw new CreditReturnHoldConflictException(
                command.SettlementReturnId);
        }

        var available = await _availabilityService.GetAvailableAsync(
            account,
            cancellationToken);

        if (command.Amount.MinorUnits > available.MinorUnits)
        {
            throw new InsufficientAvailableCreditForReturnHoldException(
                command.CreditOwnerId,
                command.Amount,
                available);
        }

        var hold = new CreditReturnHold(
            command.SettlementReturnId,
            account.Id,
            command.Amount,
            _timeProvider.GetUtcNow());

        await _holdRepository.AddAsync(
            hold,
            cancellationToken);

        return new CreditReturnHoldResult(
            hold,
            Created: true);
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Credit return hold command ID must not be empty.",
                parameterName);
        }
    }
}
