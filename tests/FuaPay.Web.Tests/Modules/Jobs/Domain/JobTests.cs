using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Jobs.Domain;

namespace FuaPay.Web.Tests.Modules.Jobs.Domain;

public sealed class JobTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset PublishedAt =
        CreatedAt.AddMinutes(1);

    private static readonly DateTimeOffset SettledAt =
        CreatedAt.AddMinutes(2);

    [Fact]
    public void NewJob_HasDraftUnpaidStateAndNormalizedText()
    {
        var job = new Job(
            Guid.NewGuid(),
            NextJobNumber(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "  Model  ",
            "  Tisk modelu  ",
            new Money(12_500),
            CreatedAt);

        Assert.Equal(JobProductionStatus.Draft, job.ProductionStatus);
        Assert.Equal(JobPaymentStatus.Unpaid, job.PaymentStatus);
        Assert.Equal("Model", job.Title);
        Assert.Equal("Tisk modelu", job.Description);
        Assert.Null(job.SettlementType);
        Assert.Null(job.SettlementReferenceId);
        Assert.Null(job.SettledAt);
    }

    [Fact]
    public void Constructor_RejectsEmptyJobId()
    {
        Action action = () =>
        {
            _ = new Job(
                Guid.Empty,
                NextJobNumber(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                "Model",
                "Tisk modelu",
                new Money(1_000),
                CreatedAt);
        };

        Assert.Throws<ArgumentException>(action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("3D-26-000001")]
    [InlineData("too-long-code-2026-000001")]
    [InlineData("3D-2026-1")]
    public void Constructor_RejectsInvalidNumber(string number)
    {
        Action action = () =>
        {
            _ = new Job(
                Guid.NewGuid(),
                number,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                "Model",
                "Tisk modelu",
                new Money(1_000),
                CreatedAt);
        };

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Constructor_RejectsEmptyServiceUnitId()
    {
        Action action = () =>
        {
            _ = new Job(
                Guid.NewGuid(),
                NextJobNumber(),
                Guid.Empty,
                Guid.NewGuid(),
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                "Model",
                "Tisk modelu",
                new Money(1_000),
                CreatedAt);
        };

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Constructor_RejectsEmptyCustomerId()
    {
        Action action = () =>
        {
            _ = new Job(
                Guid.NewGuid(),
                NextJobNumber(),
                Guid.NewGuid(),
                Guid.Empty,
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                "Model",
                "Tisk modelu",
                new Money(1_000),
                CreatedAt);
        };

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Constructor_RejectsEmptyCreatedByUserId()
    {
        Action action = () =>
        {
            _ = new Job(
                Guid.NewGuid(),
                NextJobNumber(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.Empty,
                ServiceType.ThreeDPrint,
                "Model",
                "Tisk modelu",
                new Money(1_000),
                CreatedAt);
        };

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Constructor_RejectsUnknownServiceType()
    {
        Action action = () =>
        {
            _ = new Job(
                Guid.NewGuid(),
                NextJobNumber(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ServiceType.Unknown,
                "Model",
                "Tisk modelu",
                new Money(1_000),
                CreatedAt);
        };

        Assert.Throws<ArgumentOutOfRangeException>(action);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositivePrice(
        long minorUnits)
    {
        Action action = () =>
        {
            _ = new Job(
                Guid.NewGuid(),
                NextJobNumber(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                "Model",
                "Tisk modelu",
                new Money(minorUnits),
                CreatedAt);
        };

        Assert.Throws<ArgumentOutOfRangeException>(action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankTitle(string title)
    {
        Action action = () =>
        {
            _ = new Job(
                Guid.NewGuid(),
                NextJobNumber(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                title,
                "Tisk modelu",
                new Money(1_000),
                CreatedAt);
        };

        Assert.Throws<ArgumentException>(action);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankDescription(
        string description)
    {
        Action action = () =>
        {
            _ = new Job(
                Guid.NewGuid(),
                NextJobNumber(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                "Model",
                description,
                new Money(1_000),
                CreatedAt);
        };

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void UpdateDraft_ChangesEditableFieldsAndVersion()
    {
        var job = CreateJob();
        var newCustomerId = Guid.NewGuid();

        job.UpdateDraft(
            newCustomerId,
            ServiceType.Workshop,
            "  Laser  ",
            "  Řezání překližky  ",
            new Money(25_000));

        Assert.Equal(newCustomerId, job.CustomerUserId);
        Assert.Equal(ServiceType.Workshop, job.ServiceType);
        Assert.Equal("Laser", job.Title);
        Assert.Equal("Řezání překližky", job.Description);
        Assert.Equal(new Money(25_000), job.Price);
    }

    [Fact]
    public void UpdateDraft_AfterPublishIsRejected()
    {
        var job = CreateJob();

        job.Publish(PublishedAt);

        Action action = () =>
        {
            job.UpdateDraft(
                Guid.NewGuid(),
                ServiceType.Workshop,
                "Laser",
                "Řezání překližky",
                new Money(25_000));
        };

        var exception =
            Assert.Throws<InvalidJobStateTransitionException>(
                action);

        Assert.Equal(
            JobProductionStatus.Published,
            exception.CurrentStatus);

        Assert.Equal(
            JobProductionStatus.Draft,
            exception.TargetStatus);

        Assert.Equal(new Money(12_500), job.Price);
    }

    [Fact]
    public void Publish_SetsStatusTimestampAndVersion()
    {
        var job = CreateJob();

        job.Publish(PublishedAt);

        Assert.Equal(
            JobProductionStatus.Published,
            job.ProductionStatus);

        Assert.Equal(PublishedAt, job.PublishedAt);
    }

    [Fact]
    public void Publish_BeforeCreationIsRejected()
    {
        var job = CreateJob();

        Action action = () =>
        {
            job.Publish(CreatedAt.AddTicks(-1));
        };

        Assert.Throws<ArgumentOutOfRangeException>(action);
        Assert.Equal(JobProductionStatus.Draft, job.ProductionStatus);
    }

    [Fact]
    public void ConfirmSettlement_SetsPaidState()
    {
        var job = CreatePublishedJob();
        var referenceId = Guid.NewGuid();

        var changed = job.ConfirmSettlement(
            JobSettlementType.Credit,
            referenceId,
            SettledAt);

        Assert.True(changed);
        Assert.Equal(JobPaymentStatus.Paid, job.PaymentStatus);
        Assert.Equal(JobSettlementType.Credit, job.SettlementType);
        Assert.Equal(referenceId, job.SettlementReferenceId);
        Assert.Equal(SettledAt, job.SettledAt);
    }

    [Fact]
    public void ConfirmSettlement_SameSourceIsIdempotent()
    {
        var job = CreatePublishedJob();
        var referenceId = Guid.NewGuid();

        var firstChanged = job.ConfirmSettlement(
            JobSettlementType.DirectPayment,
            referenceId,
            SettledAt);

        var secondChanged = job.ConfirmSettlement(
            JobSettlementType.DirectPayment,
            referenceId,
            SettledAt.AddMinutes(10));

        Assert.True(firstChanged);
        Assert.False(secondChanged);
        Assert.Equal(SettledAt, job.SettledAt);
    }

    [Fact]
    public void ConfirmSettlement_DifferentSecondSourceIsRejected()
    {
        var job = CreatePublishedJob();
        var firstReferenceId = Guid.NewGuid();
        var secondReferenceId = Guid.NewGuid();

        job.ConfirmSettlement(
            JobSettlementType.Credit,
            firstReferenceId,
            SettledAt);

        Action action = () =>
        {
            job.ConfirmSettlement(
                JobSettlementType.DirectPayment,
                secondReferenceId,
                SettledAt.AddMinutes(1));
        };

        var exception =
            Assert.Throws<JobSettlementConflictException>(
                action);

        Assert.Equal(
            JobSettlementType.Credit,
            exception.ExistingType);

        Assert.Equal(
            firstReferenceId,
            exception.ExistingReferenceId);

        Assert.Equal(
            JobSettlementType.DirectPayment,
            exception.AttemptedType);

        Assert.Equal(
            secondReferenceId,
            exception.AttemptedReferenceId);

        Assert.Equal(firstReferenceId, job.SettlementReferenceId);
    }

    [Fact]
    public void ConfirmSettlement_BeforePublishIsRejected()
    {
        var job = CreateJob();

        Action action = () =>
        {
            job.ConfirmSettlement(
                JobSettlementType.Credit,
                Guid.NewGuid(),
                SettledAt);
        };

        var exception =
            Assert.Throws<JobSettlementNotAllowedException>(
                action);

        Assert.Equal(
            JobProductionStatus.Draft,
            exception.ProductionStatus);

        Assert.Equal(JobPaymentStatus.Unpaid, job.PaymentStatus);
    }

    [Fact]
    public void ConfirmSettlement_BeforePublicationTimeIsRejected()
    {
        var job = CreatePublishedJob();

        Action action = () =>
        {
            job.ConfirmSettlement(
                JobSettlementType.Credit,
                Guid.NewGuid(),
                PublishedAt.AddTicks(-1));
        };

        Assert.Throws<ArgumentOutOfRangeException>(action);
        Assert.Equal(JobPaymentStatus.Unpaid, job.PaymentStatus);
    }

    [Fact]
    public void StartProduction_WithoutSettlementIsRejected()
    {
        var job = CreatePublishedJob();

        Action action = () =>
        {
            job.StartProduction(SettledAt);
        };

        Assert.Throws<JobSettlementRequiredException>(action);
        Assert.Equal(
            JobProductionStatus.Published,
            job.ProductionStatus);

    }

    [Fact]
    public void FullLifecycle_AdvancesThroughValidStates()
    {
        var job = CreatePublishedJob();
        var referenceId = Guid.NewGuid();
        var productionStartedAt = SettledAt.AddMinutes(1);
        var readyAt = productionStartedAt.AddHours(1);
        var completedAt = readyAt.AddMinutes(15);

        job.ConfirmSettlement(
            JobSettlementType.Credit,
            referenceId,
            SettledAt);

        job.StartProduction(productionStartedAt);

        Assert.Equal(
            JobProductionStatus.InProduction,
            job.ProductionStatus);

        job.MarkReadyForPickup(readyAt);

        Assert.Equal(
            JobProductionStatus.ReadyForPickup,
            job.ProductionStatus);

        job.Complete(completedAt);

        Assert.Equal(
            JobProductionStatus.Completed,
            job.ProductionStatus);

        Assert.Equal(productionStartedAt, job.ProductionStartedAt);
        Assert.Equal(readyAt, job.ReadyForPickupAt);
        Assert.Equal(completedAt, job.CompletedAt);
        Assert.Equal(JobPaymentStatus.Paid, job.PaymentStatus);
    }

    [Fact]
    public void Cancel_DraftJobSucceeds()
    {
        var job = CreateJob();
        var cancelledAt = CreatedAt.AddMinutes(1);

        job.Cancel(cancelledAt);

        Assert.Equal(
            JobProductionStatus.Cancelled,
            job.ProductionStatus);

        Assert.Equal(cancelledAt, job.CancelledAt);
        Assert.Equal(JobPaymentStatus.Unpaid, job.PaymentStatus);
    }

    [Fact]
    public void Cancel_PublishedUnpaidJobSucceeds()
    {
        var job = CreatePublishedJob();
        var cancelledAt = PublishedAt.AddMinutes(1);

        job.Cancel(cancelledAt);

        Assert.Equal(
            JobProductionStatus.Cancelled,
            job.ProductionStatus);

        Assert.Equal(cancelledAt, job.CancelledAt);
    }

    [Fact]
    public void Cancel_PaidJobIsRejected()
    {
        var job = CreatePaidJob();

        Action action = () =>
        {
            job.Cancel(SettledAt.AddMinutes(1));
        };

        Assert.Throws<
            JobCannotBeCancelledAfterSettlementException>(
                action);

        Assert.Equal(
            JobProductionStatus.Published,
            job.ProductionStatus);

        Assert.Equal(JobPaymentStatus.Paid, job.PaymentStatus);
    }

    [Fact]
    public void InvalidTransition_IsRejectedWithoutMutation()
    {
        var job = CreateJob();

        Action action = () =>
        {
            job.MarkReadyForPickup(CreatedAt.AddMinutes(1));
        };

        var exception =
            Assert.Throws<InvalidJobStateTransitionException>(
                action);

        Assert.Equal(
            JobProductionStatus.Draft,
            exception.CurrentStatus);

        Assert.Equal(
            JobProductionStatus.ReadyForPickup,
            exception.TargetStatus);

        Assert.Equal(JobProductionStatus.Draft, job.ProductionStatus);
        Assert.Null(job.ReadyForPickupAt);
    }

    [Fact]
    public void LifecycleTimestampBeforePreviousEventIsRejected()
    {
        var job = CreatePaidJob();

        Action action = () =>
        {
            job.StartProduction(SettledAt.AddTicks(-1));
        };

        Assert.Throws<ArgumentOutOfRangeException>(action);

        Assert.Equal(
            JobProductionStatus.Published,
            job.ProductionStatus);

        Assert.Null(job.ProductionStartedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_TextExceedingDomainLimit_IsRejected(
        bool titleIsTooLong)
    {
        var title = titleIsTooLong
            ? new string('T', JobTextLimits.TitleMaxLength + 1)
            : "Model";

        var description = titleIsTooLong
            ? "Tisk modelu"
            : new string(
                'D',
                JobTextLimits.DescriptionMaxLength + 1);

        Action action = () =>
        {
            _ = new Job(
                Guid.NewGuid(),
                NextJobNumber(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                title,
                description,
                new Money(12_500),
                CreatedAt);
        };

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void UpdateDraft_TextExceedingDomainLimit_DoesNotMutateJob()
    {
        var job = CreateJob();
        var originalTitle = job.Title;
        var originalDescription = job.Description;

        Action action = () =>
        {
            job.UpdateDraft(
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                new string(
                    'T',
                    JobTextLimits.TitleMaxLength + 1),
                "Nový popis",
                new Money(20_000));
        };

        Assert.Throws<ArgumentException>(action);
        Assert.Equal(originalTitle, job.Title);
        Assert.Equal(originalDescription, job.Description);
        Assert.Equal(new Money(12_500), job.Price);
    }

    private static Job CreateJob()
    {
        return new Job(
            Guid.NewGuid(),
            NextJobNumber(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServiceType.ThreeDPrint,
            "Model",
            "Tisk modelu",
            new Money(12_500),
            CreatedAt);
    }

    private static Job CreatePublishedJob()
    {
        var job = CreateJob();
        job.Publish(PublishedAt);

        return job;
    }

    private static Job CreatePaidJob()
    {
        var job = CreatePublishedJob();

        job.ConfirmSettlement(
            JobSettlementType.Credit,
            Guid.NewGuid(),
            SettledAt);

        return job;
    }
}
