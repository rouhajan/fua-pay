using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Jobs.Domain;

namespace FuaPay.Web.Modules.Jobs.Application;

public sealed class JobSettlementService
{
    private readonly IJobRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditTrail _auditTrail;

    public JobSettlementService(
        IJobRepository repository,
        TimeProvider timeProvider,
        IAuditTrail auditTrail)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(auditTrail);

        _repository = repository;
        _timeProvider = timeProvider;
        _auditTrail = auditTrail;
    }

    public async Task<bool> ConfirmAsync(
        Guid jobId,
        JobSettlementType settlementType,
        Guid settlementReferenceId,
        CancellationToken cancellationToken = default)
    {
        ValidateJobId(jobId);

        var job = await _repository.FindByIdAsync(
            jobId,
            cancellationToken);

        if (job is null)
        {
            throw new JobNotFoundException(jobId);
        }

        var now = _timeProvider.GetUtcNow();
        var wasApplied = job.ConfirmSettlement(
            settlementType,
            settlementReferenceId,
            now);

        if (!wasApplied)
        {
            return false;
        }

        _auditTrail.Stage(AuditEntry.ForProcess(
            "payment-settlement",
            "job.settled",
            "job",
            job.Id.ToString(),
            $"Zakázka {job.Number} byla uhrazena způsobem {settlementType} s referencí {settlementReferenceId}.",
            now));

        await _repository.SaveAsync(
            job,
            cancellationToken);

        return true;
    }

    private static void ValidateJobId(Guid jobId)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zakázky nesmí být prázdné.",
                nameof(jobId));
        }
    }
}
