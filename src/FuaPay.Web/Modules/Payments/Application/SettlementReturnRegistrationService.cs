using FuaPay.Web.Modules.Payments.Domain;

namespace FuaPay.Web.Modules.Payments.Application;

public sealed record SettlementReturnRegistrationResult(
    SettlementReturn SettlementReturn,
    bool Created);

public sealed class SettlementReturnRegistrationService
{
    private readonly ISettlementReturnRepository _repository;

    public SettlementReturnRegistrationService(
        ISettlementReturnRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<SettlementReturnRegistrationResult> RegisterAsync(
        SettlementReturn authoritativeSettlementReturn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authoritativeSettlementReturn);

        if (
            authoritativeSettlementReturn.State !=
            SettlementReturnState.Requested)
        {
            throw new ArgumentException(
                "Only a newly requested settlement return can be registered.",
                nameof(authoritativeSettlementReturn));
        }

        var existing = await _repository.FindByRequestIdAsync(
            authoritativeSettlementReturn.RequestId,
            cancellationToken);

        if (existing is not null)
        {
            return ResolveRequestReplay(
                authoritativeSettlementReturn,
                existing);
        }

        try
        {
            await _repository.AddAsync(
                authoritativeSettlementReturn,
                cancellationToken);

            return new SettlementReturnRegistrationResult(
                authoritativeSettlementReturn,
                Created: true);
        }
        catch (SettlementReturnRequestAlreadyExistsException)
        {
            var concurrent = await _repository.FindByRequestIdAsync(
                authoritativeSettlementReturn.RequestId,
                cancellationToken);

            if (concurrent is null)
            {
                throw;
            }

            return ResolveRequestReplay(
                authoritativeSettlementReturn,
                concurrent);
        }
        catch (SettlementReturnOriginalPaymentAlreadyExistsException)
        {
            var originalPaymentId =
                authoritativeSettlementReturn.OriginalPaymentId;

            if (!originalPaymentId.HasValue)
            {
                throw;
            }

            var concurrent =
                await _repository.FindByOriginalPaymentIdAsync(
                    originalPaymentId.Value,
                    cancellationToken);

            if (concurrent is null)
            {
                throw;
            }

            return ResolveSourceConflict(
                authoritativeSettlementReturn,
                concurrent);
        }
        catch (SettlementReturnJobAlreadyExistsException)
        {
            var jobId = authoritativeSettlementReturn.JobId;

            if (!jobId.HasValue)
            {
                throw;
            }

            var concurrent = await _repository.FindByJobIdAsync(
                jobId.Value,
                cancellationToken);

            if (concurrent is null)
            {
                throw;
            }

            return ResolveSourceConflict(
                authoritativeSettlementReturn,
                concurrent);
        }
    }

    private static SettlementReturnRegistrationResult
        ResolveSourceConflict(
            SettlementReturn candidate,
            SettlementReturn existing)
    {
        if (candidate.RequestId == existing.RequestId)
        {
            return ResolveRequestReplay(candidate, existing);
        }

        throw new SettlementReturnSourceConflictException(
            candidate.RequestId,
            existing.Id);
    }

    private static SettlementReturnRegistrationResult
        ResolveRequestReplay(
            SettlementReturn candidate,
            SettlementReturn existing)
    {
        if (!HasSameRequestPayload(candidate, existing))
        {
            throw new SettlementReturnRequestConflictException(
                candidate.RequestId);
        }

        return new SettlementReturnRegistrationResult(
            existing,
            Created: false);
    }

    private static bool HasSameRequestPayload(
        SettlementReturn candidate,
        SettlementReturn existing)
    {
        return
            candidate.RequestId == existing.RequestId &&
            candidate.Kind == existing.Kind &&
            candidate.OriginalPaymentId == existing.OriginalPaymentId &&
            candidate.JobId == existing.JobId &&
            candidate.CustomerUserId == existing.CustomerUserId &&
            candidate.AdministratorUserId ==
                existing.AdministratorUserId &&
            candidate.Amount == existing.Amount &&
            string.Equals(
                candidate.Currency,
                existing.Currency,
                StringComparison.Ordinal) &&
            string.Equals(
                candidate.Reason,
                existing.Reason,
                StringComparison.Ordinal);
    }
}
