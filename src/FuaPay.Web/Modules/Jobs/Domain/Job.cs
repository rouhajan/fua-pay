using System.Text.RegularExpressions;

using FuaPay.Web.BuildingBlocks.Domain;

namespace FuaPay.Web.Modules.Jobs.Domain;

public sealed class Job
{
    private static readonly Regex NumberPattern = new(
        "^[A-Z0-9]{2,8}-[0-9]{4}-[0-9]{6}$",
        RegexOptions.CultureInvariant);

    public Job(
        Guid id,
        string number,
        Guid serviceUnitId,
        Guid customerUserId,
        Guid createdByUserId,
        ServiceType serviceType,
        string title,
        string description,
        Money price,
        DateTimeOffset createdAt)
    {
        ValidateId(
            id,
            nameof(id),
            "ID zakázky nesmí být prázdné.");

        ValidateId(
            serviceUnitId,
            nameof(serviceUnitId),
            "ID pracoviště nesmí být prázdné.");

        ValidateId(
            customerUserId,
            nameof(customerUserId),
            "ID zákazníka nesmí být prázdné.");

        ValidateId(
            createdByUserId,
            nameof(createdByUserId),
            "ID původce zakázky nesmí být prázdné.");

        ValidateServiceType(serviceType);
        ValidatePrice(price);
        ValidateTimestamp(createdAt, nameof(createdAt));

        Id = id;
        Number = NormalizeNumber(number);
        ServiceUnitId = serviceUnitId;
        CustomerUserId = customerUserId;
        CreatedByUserId = createdByUserId;
        ServiceType = serviceType;
        Title = NormalizeRequiredText(
            title,
            nameof(title),
            JobTextLimits.TitleMaxLength);
        Description = NormalizeRequiredText(
            description,
            nameof(description),
            JobTextLimits.DescriptionMaxLength);
        Price = price;
        ProductionStatus = JobProductionStatus.Draft;
        PaymentStatus = JobPaymentStatus.Unpaid;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public string Number { get; }

    public Guid ServiceUnitId { get; }

    public Guid CustomerUserId { get; private set; }

    public Guid CreatedByUserId { get; }

    public ServiceType ServiceType { get; private set; }

    public string Title { get; private set; }

    public string Description { get; private set; }

    public Money Price { get; private set; }

    public JobProductionStatus ProductionStatus { get; private set; }

    public JobPaymentStatus PaymentStatus { get; private set; }

    public JobSettlementType? SettlementType { get; private set; }

    public Guid? SettlementReferenceId { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset? SettledAt { get; private set; }

    public DateTimeOffset? ProductionStartedAt { get; private set; }

    public DateTimeOffset? ReadyForPickupAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }


    public void UpdateDraft(
        Guid customerUserId,
        ServiceType serviceType,
        string title,
        string description,
        Money price)
    {
        EnsureTransitionAllowed(
            JobProductionStatus.Draft,
            JobProductionStatus.Draft);

        ValidateId(
            customerUserId,
            nameof(customerUserId),
            "ID zákazníka nesmí být prázdné.");

        ValidateServiceType(serviceType);
        ValidatePrice(price);

        var normalizedTitle =
            NormalizeRequiredText(
                title,
                nameof(title),
                JobTextLimits.TitleMaxLength);

        var normalizedDescription =
            NormalizeRequiredText(
                description,
                nameof(description),
                JobTextLimits.DescriptionMaxLength);

        CustomerUserId = customerUserId;
        ServiceType = serviceType;
        Title = normalizedTitle;
        Description = normalizedDescription;
        Price = price;

    }

    public void Publish(DateTimeOffset publishedAt)
    {
        EnsureTransitionAllowed(
            JobProductionStatus.Draft,
            JobProductionStatus.Published);

        ValidateTimestampNotBefore(
            publishedAt,
            CreatedAt,
            nameof(publishedAt));

        ProductionStatus = JobProductionStatus.Published;
        PublishedAt = publishedAt;

    }

    public bool ConfirmSettlement(
        JobSettlementType settlementType,
        Guid settlementReferenceId,
        DateTimeOffset settledAt)
    {
        ValidateSettlementType(settlementType);

        ValidateId(
            settlementReferenceId,
            nameof(settlementReferenceId),
            "ID zdroje úhrady nesmí být prázdné.");

        ValidateTimestamp(settledAt, nameof(settledAt));

        if (PaymentStatus == JobPaymentStatus.Paid)
        {
            if (
                SettlementType == settlementType &&
                SettlementReferenceId == settlementReferenceId
            )
            {
                return false;
            }

            throw new JobSettlementConflictException(
                SettlementType!.Value,
                SettlementReferenceId!.Value,
                settlementType,
                settlementReferenceId);
        }

        if (ProductionStatus != JobProductionStatus.Published)
        {
            throw new JobSettlementNotAllowedException(
                ProductionStatus);
        }

        ValidateTimestampNotBefore(
            settledAt,
            PublishedAt!.Value,
            nameof(settledAt));

        PaymentStatus = JobPaymentStatus.Paid;
        SettlementType = settlementType;
        SettlementReferenceId = settlementReferenceId;
        SettledAt = settledAt;


        return true;
    }

    public void StartProduction(
        DateTimeOffset productionStartedAt)
    {
        EnsureTransitionAllowed(
            JobProductionStatus.Published,
            JobProductionStatus.InProduction);

        EnsureSettled();

        ValidateTimestampNotBefore(
            productionStartedAt,
            SettledAt!.Value,
            nameof(productionStartedAt));

        ProductionStatus = JobProductionStatus.InProduction;
        ProductionStartedAt = productionStartedAt;

    }

    public void MarkReadyForPickup(
        DateTimeOffset readyForPickupAt)
    {
        EnsureTransitionAllowed(
            JobProductionStatus.InProduction,
            JobProductionStatus.ReadyForPickup);

        EnsureSettled();

        ValidateTimestampNotBefore(
            readyForPickupAt,
            ProductionStartedAt!.Value,
            nameof(readyForPickupAt));

        ProductionStatus =
            JobProductionStatus.ReadyForPickup;

        ReadyForPickupAt = readyForPickupAt;

    }

    public void Complete(DateTimeOffset completedAt)
    {
        EnsureTransitionAllowed(
            JobProductionStatus.ReadyForPickup,
            JobProductionStatus.Completed);

        EnsureSettled();

        ValidateTimestampNotBefore(
            completedAt,
            ReadyForPickupAt!.Value,
            nameof(completedAt));

        ProductionStatus = JobProductionStatus.Completed;
        CompletedAt = completedAt;

    }

    public void Cancel(DateTimeOffset cancelledAt)
    {
        ValidateTimestamp(cancelledAt, nameof(cancelledAt));

        if (PaymentStatus == JobPaymentStatus.Paid)
        {
            throw new JobCannotBeCancelledAfterSettlementException();
        }

        if (
            ProductionStatus != JobProductionStatus.Draft &&
            ProductionStatus != JobProductionStatus.Published
        )
        {
            throw new InvalidJobStateTransitionException(
                ProductionStatus,
                JobProductionStatus.Cancelled);
        }

        var minimumTimestamp =
            PublishedAt ?? CreatedAt;

        ValidateTimestampNotBefore(
            cancelledAt,
            minimumTimestamp,
            nameof(cancelledAt));

        ProductionStatus = JobProductionStatus.Cancelled;
        CancelledAt = cancelledAt;

    }

    private void EnsureSettled()
    {
        if (PaymentStatus != JobPaymentStatus.Paid)
        {
            throw new JobSettlementRequiredException();
        }
    }

    private void EnsureTransitionAllowed(
        JobProductionStatus requiredCurrentStatus,
        JobProductionStatus targetStatus)
    {
        if (ProductionStatus != requiredCurrentStatus)
        {
            throw new InvalidJobStateTransitionException(
                ProductionStatus,
                targetStatus);
        }
    }


    private static void ValidateId(
        Guid value,
        string parameterName,
        string message)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                message,
                parameterName);
        }
    }


    private static string NormalizeNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Číslo zakázky nesmí být prázdné.",
                nameof(value));
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (!NumberPattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "Číslo zakázky musí mít tvar KÓD-ROK-POŘADÍ.",
                nameof(value));
        }

        return normalized;
    }

    private static void ValidateServiceType(
        ServiceType serviceType)
    {
        if (
            serviceType == ServiceType.Unknown ||
            !Enum.IsDefined(
                typeof(ServiceType),
                serviceType)
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(serviceType),
                "Druh služby není podporovaný.");
        }
    }

    private static void ValidateSettlementType(
        JobSettlementType settlementType)
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
    }

    private static void ValidatePrice(Money price)
    {
        if (!FinancialAmountPolicy.JobPrice.Contains(price))
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                "Cena zakázky musí být mezi 0,01 Kč a 1 000 000 Kč.");
        }
    }

    private static string NormalizeRequiredText(
        string value,
        string parameterName,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Textová hodnota nesmí být prázdná.",
                parameterName);
        }

        var normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new ArgumentException(
                $"Textová hodnota může mít nejvýše {maxLength} znaků.",
                parameterName);
        }

        return normalized;
    }

    private static void ValidateTimestamp(
        DateTimeOffset timestamp,
        string parameterName)
    {
        if (timestamp == default)
        {
            throw new ArgumentException(
                "Časový údaj nesmí být prázdný.",
                parameterName);
        }
    }

    private static void ValidateTimestampNotBefore(
        DateTimeOffset timestamp,
        DateTimeOffset minimum,
        string parameterName)
    {
        ValidateTimestamp(timestamp, parameterName);

        if (timestamp < minimum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Časový údaj nesmí předcházet předchozí události.");
        }
    }
}
