namespace FuaPay.Web.Modules.Jobs.Domain;

public sealed class InvalidJobStateTransitionException
    : InvalidOperationException
{
    public InvalidJobStateTransitionException(
        JobProductionStatus currentStatus,
        JobProductionStatus targetStatus)
        : base(
            $"Zakázku nelze převést ze stavu " +
            $"{currentStatus} do stavu {targetStatus}.")
    {
        CurrentStatus = currentStatus;
        TargetStatus = targetStatus;
    }

    public JobProductionStatus CurrentStatus { get; }

    public JobProductionStatus TargetStatus { get; }
}

public sealed class JobSettlementRequiredException
    : InvalidOperationException
{
    public JobSettlementRequiredException()
        : base(
            "Zakázka musí být před touto operací uhrazena.")
    {
    }
}

public sealed class JobSettlementNotAllowedException
    : InvalidOperationException
{
    public JobSettlementNotAllowedException(
        JobProductionStatus productionStatus)
        : base(
            "Úhradu lze poprvé potvrdit pouze u zveřejněné zakázky.")
    {
        ProductionStatus = productionStatus;
    }

    public JobProductionStatus ProductionStatus { get; }
}

public sealed class JobSettlementConflictException
    : InvalidOperationException
{
    public JobSettlementConflictException(
        JobSettlementType existingType,
        Guid existingReferenceId,
        JobSettlementType attemptedType,
        Guid attemptedReferenceId)
        : base(
            "Zakázka již byla uhrazena jiným zdrojem.")
    {
        ExistingType = existingType;
        ExistingReferenceId = existingReferenceId;
        AttemptedType = attemptedType;
        AttemptedReferenceId = attemptedReferenceId;
    }

    public JobSettlementType ExistingType { get; }

    public Guid ExistingReferenceId { get; }

    public JobSettlementType AttemptedType { get; }

    public Guid AttemptedReferenceId { get; }
}

public sealed class JobCannotBeCancelledAfterSettlementException
    : InvalidOperationException
{
    public JobCannotBeCancelledAfterSettlementException()
        : base(
            "Uhrazenou zakázku nelze zrušit bez procesu storna nebo vratky.")
    {
    }
}
