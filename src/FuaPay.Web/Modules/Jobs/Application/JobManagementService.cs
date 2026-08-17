using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Notifications;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;

namespace FuaPay.Web.Modules.Jobs.Application;

public sealed class JobManagementService
{
    private readonly IJobRepository _repository;
    private readonly IJobNumberAllocator _numberAllocator;
    private readonly IServiceUnitQueries _serviceUnitQueries;
    private readonly IAccessUserQueries _accessUserQueries;
    private readonly TimeProvider _timeProvider;
    private readonly IAuditTrail _auditTrail;
    private readonly INotificationOutbox _notificationOutbox;

    public JobManagementService(
        IJobRepository repository,
        IJobNumberAllocator numberAllocator,
        IServiceUnitQueries serviceUnitQueries,
        IAccessUserQueries accessUserQueries,
        TimeProvider timeProvider,
        IAuditTrail auditTrail,
        INotificationOutbox notificationOutbox)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(numberAllocator);
        ArgumentNullException.ThrowIfNull(serviceUnitQueries);
        ArgumentNullException.ThrowIfNull(accessUserQueries);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(notificationOutbox);

        _repository = repository;
        _numberAllocator = numberAllocator;
        _serviceUnitQueries = serviceUnitQueries;
        _accessUserQueries = accessUserQueries;
        _timeProvider = timeProvider;
        _auditTrail = auditTrail;
        _notificationOutbox = notificationOutbox;
    }

    public async Task<Job> CreateDraftAsync(
        JobManagementActor actor,
        Guid serviceUnitId,
        Guid customerUserId,
        ServiceType serviceType,
        string title,
        string description,
        Money price,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ValidatePrice(price);

        if (!actor.CanManage(serviceUnitId))
        {
            throw new JobServiceUnitAccessDeniedException(
                serviceUnitId,
                actor.UserId);
        }

        var serviceUnit =
            await _serviceUnitQueries.FindActiveAsync(
                serviceUnitId,
                cancellationToken)
            ?? throw new JobServiceUnitUnavailableException(
                serviceUnitId);

        ValidateServiceTypeForUnit(serviceUnit, serviceType);
        await EnsureActiveCustomerAsync(
            customerUserId,
            cancellationToken);

        var now = _timeProvider.GetUtcNow();

        var number = await _numberAllocator.AllocateAsync(
            serviceUnit.Id,
            serviceUnit.Code,
            now.Year,
            cancellationToken);

        var job = new Job(
            Guid.NewGuid(),
            number,
            serviceUnit.Id,
            customerUserId,
            actor.UserId,
            serviceType,
            title,
            description,
            price,
            now);

        StageAudit(
            actor,
            "job.created",
            job,
            $"Zakázka {job.Number} byla vytvořena jako koncept.",
            now);

        await _repository.AddAsync(
            job,
            cancellationToken);

        return job;
    }

    public async Task<Job> UpdateDraftAsync(
        JobManagementActor actor,
        Guid jobId,
        Guid customerUserId,
        ServiceType serviceType,
        string title,
        string description,
        Money price,
        CancellationToken cancellationToken = default)
    {
        ValidatePrice(price);
        var job = await LoadManagedJobAsync(
            actor,
            jobId,
            cancellationToken);

        var serviceUnit =
            await _serviceUnitQueries.FindActiveAsync(
                job.ServiceUnitId,
                cancellationToken)
            ?? throw new JobServiceUnitUnavailableException(
                job.ServiceUnitId);

        ValidateServiceTypeForUnit(serviceUnit, serviceType);
        await EnsureActiveCustomerAsync(
            customerUserId,
            cancellationToken);

        job.UpdateDraft(
            customerUserId,
            serviceType,
            title,
            description,
            price);

        StageAudit(
            actor,
            "job.updated",
            job,
            $"Koncept zakázky {job.Number} byl upraven.",
            _timeProvider.GetUtcNow());

        await _repository.SaveAsync(
            job,
            cancellationToken);

        return job;
    }

    public async Task<Job> PublishAsync(
        JobManagementActor actor,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await LoadManagedJobAsync(
            actor,
            jobId,
            cancellationToken);

        var now = _timeProvider.GetUtcNow();
        job.Publish(now);
        StageAudit(
            actor,
            "job.published",
            job,
            $"Zakázka {job.Number} byla zveřejněna zákazníkovi.",
            now);
        StageCustomerNotification(
            job,
            "job.published",
            $"Zakázka {job.Number} čeká na úhradu",
            $"Zakázka {job.Number} byla zveřejněna a čeká na úhradu částky {job.Price.MinorUnits / 100m:0.00} Kč.",
            now);

        await _repository.SaveAsync(
            job,
            cancellationToken);

        return job;
    }

    public async Task<Job> CancelAsync(
        JobManagementActor actor,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await LoadManagedJobAsync(
            actor,
            jobId,
            cancellationToken);

        var now = _timeProvider.GetUtcNow();
        job.Cancel(now);
        StageAudit(
            actor,
            "job.cancelled",
            job,
            $"Zakázka {job.Number} byla zrušena.",
            now);
        StageCustomerNotification(
            job,
            "job.cancelled",
            $"Zakázka {job.Number} byla zrušena",
            $"Zakázka {job.Number} byla zrušena.",
            now);

        await _repository.SaveAsync(
            job,
            cancellationToken);

        return job;
    }

    public async Task<Job> StartProductionAsync(
        JobManagementActor actor,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await LoadManagedJobAsync(
            actor,
            jobId,
            cancellationToken);

        var now = _timeProvider.GetUtcNow();
        job.StartProduction(now);
        StageAudit(
            actor,
            "job.production-started",
            job,
            $"U zakázky {job.Number} byla zahájena výroba.",
            now);

        await _repository.SaveAsync(
            job,
            cancellationToken);

        return job;
    }

    public async Task<Job> MarkReadyForPickupAsync(
        JobManagementActor actor,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await LoadManagedJobAsync(
            actor,
            jobId,
            cancellationToken);

        var now = _timeProvider.GetUtcNow();
        job.MarkReadyForPickup(now);
        StageAudit(
            actor,
            "job.ready-for-pickup",
            job,
            $"Zakázka {job.Number} byla označena jako připravená k vyzvednutí.",
            now);
        StageCustomerNotification(
            job,
            "job.ready-for-pickup",
            $"Zakázka {job.Number} je připravena",
            $"Zakázka {job.Number} je připravena k vyzvednutí.",
            now);

        await _repository.SaveAsync(
            job,
            cancellationToken);

        return job;
    }

    public async Task<Job> CompleteAsync(
        JobManagementActor actor,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await LoadManagedJobAsync(
            actor,
            jobId,
            cancellationToken);

        var now = _timeProvider.GetUtcNow();
        job.Complete(now);
        StageAudit(
            actor,
            "job.completed",
            job,
            $"Zakázka {job.Number} byla dokončena.",
            now);

        await _repository.SaveAsync(
            job,
            cancellationToken);

        return job;
    }


    private void StageAudit(
        JobManagementActor actor,
        string action,
        Job job,
        string description,
        DateTimeOffset occurredAt)
    {
        _auditTrail.Stage(AuditEntry.ForUser(
            actor.UserId,
            action,
            "job",
            job.Id.ToString(),
            description,
            occurredAt));
    }


    private void StageCustomerNotification(
        Job job,
        string type,
        string subject,
        string body,
        DateTimeOffset createdAt)
    {
        _notificationOutbox.Stage(NotificationMessage.Create(
            job.CustomerUserId,
            type,
            subject,
            body,
            createdAt));
    }

    private async Task EnsureActiveCustomerAsync(
        Guid customerUserId,
        CancellationToken cancellationToken)
    {
        if (customerUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zákazníka nesmí být prázdné.",
                nameof(customerUserId));
        }

        if (!await _accessUserQueries.IsActiveCustomerAsync(
            customerUserId,
            cancellationToken))
        {
            throw new JobCustomerUnavailableException(
                customerUserId);
        }
    }

    private static void ValidateServiceTypeForUnit(
        ServiceUnitReadModel serviceUnit,
        ServiceType serviceType)
    {
        if (serviceType != serviceUnit.DefaultServiceType)
        {
            throw new ServiceTypeMismatchException(
                serviceUnit.Id,
                serviceUnit.DefaultServiceType,
                serviceType);
        }
    }

    private static void ValidatePrice(Money price)
    {
        if (!FinancialAmountPolicy.JobPrice.Contains(price))
        {
            throw new JobPriceNotAllowedException();
        }
    }

    private async Task<Job> LoadManagedJobAsync(
        JobManagementActor actor,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ValidateJobId(jobId);

        var job = await _repository.FindByIdAsync(
            jobId,
            cancellationToken);

        if (job is null)
        {
            throw new JobNotFoundException(jobId);
        }

        if (!actor.CanManage(job.ServiceUnitId))
        {
            throw new JobAccessDeniedException(
                job.Id,
                actor.UserId);
        }

        return job;
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
