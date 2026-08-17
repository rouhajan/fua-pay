using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.ServiceUnits.Domain;

namespace FuaPay.Web.Development;

public sealed record DevelopmentDataUserIds(
    Guid PrimaryCustomer,
    Guid LowCreditCustomer,
    Guid ThreeDPrintRequester,
    Guid WorkshopRequester,
    Guid PlotterRequester,
    Guid SecretariatRequesterA,
    Guid SecretariatRequesterB,
    Guid SecretariatRequesterC,
    Guid Administrator)
{
    public IReadOnlyCollection<Guid> All =>
    [
        PrimaryCustomer,
        LowCreditCustomer,
        ThreeDPrintRequester,
        WorkshopRequester,
        PlotterRequester,
        SecretariatRequesterA,
        SecretariatRequesterB,
        SecretariatRequesterC,
        Administrator
    ];
}

public static class DevelopmentDataScenario
{
    public const int JobNumberYear = 2026;

    public const string CompletedJobNumber =
        "DIL-2026-000001";

    public const string CancelledJobNumber =
        "3D-2026-000001";

    public const string ReadyJobNumber =
        "PLT-2026-000001";

    public const string InProductionJobNumber =
        "3D-2026-000002";

    public const string PublishedUnpaidJobNumber =
        "PLT-2026-000002";

    public const string DraftJobNumber =
        "SEK-2026-000001";

    public static readonly Guid PrimaryCustomerCreditAccountId =
        Guid.Parse("6d53ac66-07e7-4f54-a969-f55375347831");

    public static readonly Guid PrimaryCustomerInitialCreditOperationId =
        Guid.Parse("85383bd7-6ab1-4e3b-9e19-42eed9b911aa");

    public static readonly Guid LowCreditCustomerCreditAccountId =
        Guid.Parse("11c22fbd-4cbe-4422-a991-938857a6c965");

    public static readonly Guid LowCreditCustomerInitialCreditOperationId =
        Guid.Parse("e2177f46-37f3-4a93-9d03-b9f2e14a65c3");

    public static readonly Guid CompletedJobId =
        Guid.Parse("8cc9d7e4-71cd-46a9-8eb1-9ac9af8c91ee");

    public static readonly Guid CancelledJobId =
        Guid.Parse("09a59aa4-c857-44a1-a9d2-8ec04c48a937");

    public static readonly Guid ReadyJobId =
        Guid.Parse("f0ca070f-0719-4a76-aa7a-8178dcac65fd");

    public static readonly Guid InProductionJobId =
        Guid.Parse("8bf7ac69-7a16-4be6-8822-c4188de62344");

    public static readonly Guid PublishedUnpaidJobId =
        Guid.Parse("a15fcbae-8774-49d5-a5d5-d923d3e43983");

    public static readonly Guid DraftJobId =
        Guid.Parse("2c4b55bd-5030-4502-9c31-db6c04469d8a");

    public static readonly Guid ThreeDPrintServiceUnitId =
        Guid.Parse("0d801ca7-d05d-4ee2-b9a5-6037a8287528");

    public static readonly Guid PlotterServiceUnitId =
        Guid.Parse("a62d70d1-d4cb-49c4-b822-39f9b82ea0dc");

    public static readonly Guid WorkshopServiceUnitId =
        Guid.Parse("4284df67-6bb2-437d-87a1-cb4664fe99eb");

    public static readonly Guid SecretariatServiceUnitId =
        Guid.Parse("c7ee2ade-4a59-4a3d-aa71-dd27a031dca3");

    public static readonly Guid ThreeDPrintRequesterAssignmentId =
        Guid.Parse("e7da34f8-ca6b-4a4c-9fa5-9f4e4d3937aa");

    public static readonly Guid WorkshopRequesterAssignmentId =
        Guid.Parse("c6daee8b-f5d9-4a3a-8b6d-a84297e277a1");

    public static readonly Guid PlotterRequesterAssignmentId =
        Guid.Parse("53680daa-8ce6-422c-9bff-8271813f2f16");

    public static readonly Guid SecretariatRequesterAAssignmentId =
        Guid.Parse("809ce321-4d6a-488f-aeec-684ef29f2e4c");

    public static readonly Guid SecretariatRequesterBAssignmentId =
        Guid.Parse("3271060f-53ff-4863-9f0d-6c583647aee6");

    public static readonly Guid SecretariatRequesterCAssignmentId =
        Guid.Parse("fb596bab-0e08-4b51-836e-e623e95f5c87");

    public static IReadOnlyCollection<Guid> ServiceUnitIds =>
    [
        ThreeDPrintServiceUnitId,
        PlotterServiceUnitId,
        WorkshopServiceUnitId,
        SecretariatServiceUnitId
    ];

    public static IReadOnlyDictionary<Guid, int>
        JobNumberSequenceMinimums
    { get; } =
            new Dictionary<Guid, int>
            {
                [ThreeDPrintServiceUnitId] = 2,
                [PlotterServiceUnitId] = 2,
                [WorkshopServiceUnitId] = 1,
                [SecretariatServiceUnitId] = 1
            };

    private static readonly DateTimeOffset BaseTime =
        new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    public static IReadOnlyList<ServiceUnit> CreateServiceUnits()
    {
        var actor = ServiceUnitChangeActor.ForProcess(
            "development-data");

        return
        [
            new ServiceUnit(
                ThreeDPrintServiceUnitId,
                "3D",
                "3D tisk",
                ServiceType.ThreeDPrint,
                BaseTime.AddDays(-2),
                actor),
            new ServiceUnit(
                PlotterServiceUnitId,
                "PLT",
                "Plotr",
                ServiceType.LargeFormatPrint,
                BaseTime.AddDays(-2),
                actor),
            new ServiceUnit(
                WorkshopServiceUnitId,
                "DIL",
                "Dílna",
                ServiceType.Workshop,
                BaseTime.AddDays(-2),
                actor),
            new ServiceUnit(
                SecretariatServiceUnitId,
                "SEK",
                "Sekretariát",
                ServiceType.Other,
                BaseTime.AddDays(-2),
                actor)
        ];
    }

    public static IReadOnlyList<RequesterServiceUnitAssignment>
        CreateServiceUnitAssignments(
            DevelopmentDataUserIds users)
    {
        ArgumentNullException.ThrowIfNull(users);

        var actor = ServiceUnitChangeActor.ForProcess(
            "development-data");

        var grantedAt = BaseTime.AddDays(-1);

        return
        [
            new RequesterServiceUnitAssignment(
                ThreeDPrintRequesterAssignmentId,
                ThreeDPrintServiceUnitId,
                users.ThreeDPrintRequester,
                grantedAt,
                actor),
            new RequesterServiceUnitAssignment(
                WorkshopRequesterAssignmentId,
                WorkshopServiceUnitId,
                users.WorkshopRequester,
                grantedAt,
                actor),
            new RequesterServiceUnitAssignment(
                PlotterRequesterAssignmentId,
                PlotterServiceUnitId,
                users.PlotterRequester,
                grantedAt,
                actor),
            new RequesterServiceUnitAssignment(
                SecretariatRequesterAAssignmentId,
                SecretariatServiceUnitId,
                users.SecretariatRequesterA,
                grantedAt,
                actor),
            new RequesterServiceUnitAssignment(
                SecretariatRequesterBAssignmentId,
                SecretariatServiceUnitId,
                users.SecretariatRequesterB,
                grantedAt,
                actor),
            new RequesterServiceUnitAssignment(
                SecretariatRequesterCAssignmentId,
                SecretariatServiceUnitId,
                users.SecretariatRequesterC,
                grantedAt,
                actor)
        ];
    }

    public static IReadOnlyList<CreditAccount>
        CreateCustomerCreditAccounts(
            DevelopmentDataUserIds users)
    {
        ArgumentNullException.ThrowIfNull(users);

        return
        [
            CreatePrimaryCustomerCreditAccount(users.PrimaryCustomer),
            CreateLowCreditCustomerCreditAccount(users.LowCreditCustomer)
        ];
    }

    public static IReadOnlyList<Job> CreateJobs(
        DevelopmentDataUserIds users)
    {
        ArgumentNullException.ThrowIfNull(users);

        return
        [
            CreateCompletedJob(
                users.PrimaryCustomer,
                users.WorkshopRequester),
            CreateCancelledJob(
                users.PrimaryCustomer,
                users.ThreeDPrintRequester),
            CreateReadyJob(
                users.PrimaryCustomer,
                users.PlotterRequester),
            CreateInProductionJob(
                users.PrimaryCustomer,
                users.ThreeDPrintRequester),
            CreatePublishedUnpaidJob(
                users.LowCreditCustomer,
                users.PlotterRequester),
            CreateDraftJob(
                users.LowCreditCustomer,
                users.SecretariatRequesterA)
        ];
    }

    private static CreditAccount CreatePrimaryCustomerCreditAccount(
        Guid customerUserId)
    {
        var account = new CreditAccount(
            PrimaryCustomerCreditAccountId,
            customerUserId);

        account.Credit(
            PrimaryCustomerInitialCreditOperationId,
            Money.FromCrowns(2000m),
            BaseTime.AddDays(-1),
            "Testovací dobití kreditu");

        account.Debit(
            CompletedJobId,
            Money.FromCrowns(260m),
            BaseTime.AddHours(2),
            $"Úhrada zakázky {CompletedJobId}");

        account.Debit(
            ReadyJobId,
            Money.FromCrowns(140m),
            BaseTime.AddDays(6).AddHours(2),
            $"Úhrada zakázky {ReadyJobId}");

        account.Debit(
            InProductionJobId,
            Money.FromCrowns(390m),
            BaseTime.AddDays(10).AddHours(2),
            $"Úhrada zakázky {InProductionJobId}");

        return account;
    }

    private static CreditAccount CreateLowCreditCustomerCreditAccount(
        Guid customerUserId)
    {
        var account = new CreditAccount(
            LowCreditCustomerCreditAccountId,
            customerUserId);

        account.Credit(
            LowCreditCustomerInitialCreditOperationId,
            Money.FromCrowns(200m),
            BaseTime.AddDays(-1),
            "Testovací nízký kredit");

        return account;
    }

    private static Job CreateCompletedJob(
        Guid customerUserId,
        Guid requesterUserId)
    {
        var job = new Job(
            CompletedJobId,
            CompletedJobNumber,
            WorkshopServiceUnitId,
            customerUserId,
            requesterUserId,
            ServiceType.Workshop,
            "Výřez prezentačního panelu",
            "Výřez a dokončení prezentačního panelu v dílně.",
            Money.FromCrowns(260m),
            BaseTime);

        job.Publish(BaseTime.AddHours(1));
        job.ConfirmSettlement(
            JobSettlementType.Credit,
            CompletedJobId,
            BaseTime.AddHours(2));
        job.StartProduction(BaseTime.AddHours(3));
        job.MarkReadyForPickup(BaseTime.AddDays(2));
        job.Complete(BaseTime.AddDays(3));

        return job;
    }

    private static Job CreateCancelledJob(
        Guid customerUserId,
        Guid requesterUserId)
    {
        var createdAt = BaseTime.AddDays(4);

        var job = new Job(
            CancelledJobId,
            CancelledJobNumber,
            ThreeDPrintServiceUnitId,
            customerUserId,
            requesterUserId,
            ServiceType.ThreeDPrint,
            "Zkušební tisk makety",
            "Zkušební tisk makety, který byl před úhradou zrušen.",
            Money.FromCrowns(210m),
            createdAt);

        job.Publish(createdAt.AddHours(1));
        job.Cancel(createdAt.AddHours(2));

        return job;
    }

    private static Job CreateReadyJob(
        Guid customerUserId,
        Guid requesterUserId)
    {
        var createdAt = BaseTime.AddDays(6);

        var job = new Job(
            ReadyJobId,
            ReadyJobNumber,
            PlotterServiceUnitId,
            customerUserId,
            requesterUserId,
            ServiceType.LargeFormatPrint,
            "Tisk výstavního plakátu",
            "Barevný tisk výstavního plakátu ve formátu B0.",
            Money.FromCrowns(140m),
            createdAt);

        job.Publish(createdAt.AddHours(1));
        job.ConfirmSettlement(
            JobSettlementType.Credit,
            ReadyJobId,
            createdAt.AddHours(2));
        job.StartProduction(createdAt.AddHours(3));
        job.MarkReadyForPickup(createdAt.AddDays(1));

        return job;
    }

    private static Job CreateInProductionJob(
        Guid customerUserId,
        Guid requesterUserId)
    {
        var createdAt = BaseTime.AddDays(10);

        var job = new Job(
            InProductionJobId,
            InProductionJobNumber,
            ThreeDPrintServiceUnitId,
            customerUserId,
            requesterUserId,
            ServiceType.ThreeDPrint,
            "3D tisk modelu ateliéru",
            "3D tisk prezentačního modelu ateliéru z PLA.",
            Money.FromCrowns(390m),
            createdAt);

        job.Publish(createdAt.AddHours(1));
        job.ConfirmSettlement(
            JobSettlementType.Credit,
            InProductionJobId,
            createdAt.AddHours(2));
        job.StartProduction(createdAt.AddHours(3));

        return job;
    }

    private static Job CreatePublishedUnpaidJob(
        Guid customerUserId,
        Guid requesterUserId)
    {
        var createdAt = BaseTime.AddDays(14);

        var job = new Job(
            PublishedUnpaidJobId,
            PublishedUnpaidJobNumber,
            PlotterServiceUnitId,
            customerUserId,
            requesterUserId,
            ServiceType.LargeFormatPrint,
            "Tisk závěrečné prezentace",
            "Velkoformátový tisk prezentačních panelů.",
            Money.FromCrowns(520m),
            createdAt);

        job.Publish(createdAt.AddHours(1));

        return job;
    }

    private static Job CreateDraftJob(
        Guid customerUserId,
        Guid requesterUserId)
    {
        var createdAt = BaseTime.AddDays(16);

        return new Job(
            DraftJobId,
            DraftJobNumber,
            SecretariatServiceUnitId,
            customerUserId,
            requesterUserId,
            ServiceType.Other,
            "Administrativní poplatek",
            "Koncept testovací zakázky sekretariátu.",
            Money.FromCrowns(80m),
            createdAt);
    }
}
