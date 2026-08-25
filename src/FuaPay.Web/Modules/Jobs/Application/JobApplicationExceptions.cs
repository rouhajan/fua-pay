using FuaPay.Web.Modules.Jobs.Domain;

namespace FuaPay.Web.Modules.Jobs.Application;

public sealed class JobNotFoundException :
    InvalidOperationException
{
    public JobNotFoundException(Guid jobId)
        : base($"Zakázka '{jobId}' nebyla nalezena.")
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zakázky nesmí být prázdné.",
                nameof(jobId));
        }

        JobId = jobId;
    }

    public Guid JobId { get; }
}

public sealed class JobAccessDeniedException :
    InvalidOperationException
{
    public JobAccessDeniedException(
        Guid jobId,
        Guid actorUserId)
        : base(
            $"Uživatel '{actorUserId}' nesmí spravovat " +
            $"zakázku '{jobId}'.")
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zakázky nesmí být prázdné.",
                nameof(jobId));
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(actorUserId));
        }

        JobId = jobId;
        ActorUserId = actorUserId;
    }

    public Guid JobId { get; }

    public Guid ActorUserId { get; }
}

public sealed class JobPaymentAccessDeniedException :
    InvalidOperationException
{
    public JobPaymentAccessDeniedException(
        Guid jobId,
        Guid customerUserId)
        : base(
            $"Uživatel '{customerUserId}' nesmí uhradit " +
            $"zakázku '{jobId}'.")
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zakázky nesmí být prázdné.",
                nameof(jobId));
        }

        if (customerUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zákazníka nesmí být prázdné.",
                nameof(customerUserId));
        }

        JobId = jobId;
        CustomerUserId = customerUserId;
    }

    public Guid JobId { get; }

    public Guid CustomerUserId { get; }
}

public sealed class JobPaymentInProgressException :
    InvalidOperationException
{
    public JobPaymentInProgressException(Guid jobId)
        : base(
            $"Zakázka '{jobId}' má otevřený pokus o přímou platbu.")
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zakázky nesmí být prázdné.",
                nameof(jobId));
        }

        JobId = jobId;
    }

    public Guid JobId { get; }
}

public sealed class JobConcurrencyException :
    InvalidOperationException
{
    public JobConcurrencyException(
        Guid jobId,
        Exception? innerException = null)
        : base(
            $"Zakázka '{jobId}' byla souběžně změněna " +
            "jiným požadavkem.",
            innerException)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zakázky nesmí být prázdné.",
                nameof(jobId));
        }

        JobId = jobId;
    }

    public Guid JobId { get; }
}

public sealed class JobSettlementReferenceAlreadyUsedException :
    InvalidOperationException
{
    public JobSettlementReferenceAlreadyUsedException(
        JobSettlementType settlementType,
        Guid settlementReferenceId,
        Exception? innerException = null)
        : base(
            $"Zdroj úhrady '{settlementType}/" +
            $"{settlementReferenceId}' již používá jiná zakázka.",
            innerException)
    {
        if (
            settlementType == JobSettlementType.Unknown ||
            !Enum.IsDefined(
                typeof(JobSettlementType),
                settlementType)
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(settlementType),
                "Druh zdroje úhrady není podporovaný.");
        }

        if (settlementReferenceId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zdroje úhrady nesmí být prázdné.",
                nameof(settlementReferenceId));
        }

        SettlementType = settlementType;
        SettlementReferenceId = settlementReferenceId;
    }

    public JobSettlementType SettlementType { get; }

    public Guid SettlementReferenceId { get; }
}

public sealed class JobNumberAlreadyUsedException :
    InvalidOperationException
{
    public JobNumberAlreadyUsedException(
        string jobNumber,
        Exception? innerException = null)
        : base(
            $"Číslo zakázky '{jobNumber}' již používá jiná zakázka.",
            innerException)
    {
        if (string.IsNullOrWhiteSpace(jobNumber))
        {
            throw new ArgumentException(
                "Číslo zakázky nesmí být prázdné.",
                nameof(jobNumber));
        }

        JobNumber = jobNumber;
    }

    public string JobNumber { get; }
}

public sealed class JobServiceUnitUnavailableException :
    InvalidOperationException
{
    public JobServiceUnitUnavailableException(Guid serviceUnitId)
        : base(
            $"Pracoviště '{serviceUnitId}' není aktivní nebo neexistuje.")
    {
        if (serviceUnitId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID pracoviště nesmí být prázdné.",
                nameof(serviceUnitId));
        }

        ServiceUnitId = serviceUnitId;
    }

    public Guid ServiceUnitId { get; }
}

public sealed class JobServiceUnitAccessDeniedException :
    InvalidOperationException
{
    public JobServiceUnitAccessDeniedException(
        Guid serviceUnitId,
        Guid actorUserId)
        : base(
            $"Uživatel '{actorUserId}' nesmí spravovat zakázky " +
            $"pracoviště '{serviceUnitId}'.")
    {
        if (serviceUnitId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID pracoviště nesmí být prázdné.",
                nameof(serviceUnitId));
        }

        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(actorUserId));
        }

        ServiceUnitId = serviceUnitId;
        ActorUserId = actorUserId;
    }

    public Guid ServiceUnitId { get; }

    public Guid ActorUserId { get; }
}


public sealed class JobCustomerUnavailableException :
    InvalidOperationException
{
    public JobCustomerUnavailableException(Guid customerUserId)
        : base(
            $"Zákazník '{customerUserId}' neexistuje, je zablokovaný " +
            "nebo nemá aktivní zákaznickou roli.")
    {
        if (customerUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zákazníka nesmí být prázdné.",
                nameof(customerUserId));
        }

        CustomerUserId = customerUserId;
    }

    public Guid CustomerUserId { get; }
}

public sealed class ServiceTypeMismatchException :
    InvalidOperationException
{
    public ServiceTypeMismatchException(
        Guid serviceUnitId,
        ServiceType expected,
        ServiceType actual)
        : base(
            $"Pracoviště '{serviceUnitId}' přijímá druh služby " +
            $"'{expected}', nikoli '{actual}'.")
    {
        ServiceUnitId = serviceUnitId;
        Expected = expected;
        Actual = actual;
    }

    public Guid ServiceUnitId { get; }

    public ServiceType Expected { get; }

    public ServiceType Actual { get; }
}

public sealed class JobPriceNotAllowedException : InvalidOperationException
{
    public JobPriceNotAllowedException()
        : base("Cena zakázky je mimo povolený rozsah.")
    {
    }
}
