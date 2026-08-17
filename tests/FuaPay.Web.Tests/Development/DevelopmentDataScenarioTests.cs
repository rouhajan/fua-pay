using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Development;
using FuaPay.Web.Modules.Jobs.Domain;

namespace FuaPay.Web.Tests.Development;

public sealed class DevelopmentDataScenarioTests
{
    private static readonly DevelopmentDataUserIds Users =
        new(
            Guid.Parse("5505afff-5153-456b-846b-b074a4db8a40"),
            Guid.Parse("428f3737-6ab7-48f7-99e7-0ddc2a80a7c0"),
            Guid.Parse("075c81c6-b37e-4354-a0b7-53d057e21ef0"),
            Guid.Parse("4bdba3ac-fb73-498e-8675-664434b57186"),
            Guid.Parse("2ac3af2f-9b91-4c8d-825c-90d00c030818"),
            Guid.Parse("6fcb49cb-7700-42bf-82ba-4cb67c77787e"),
            Guid.Parse("93994de8-f961-4ad9-b354-093a68ec48fe"),
            Guid.Parse("f4c0ea3a-30cb-46da-a4c0-c911fe7663c2"),
            Guid.Parse("3ea4dbf8-80c2-4ccd-868b-d1f68d1189ee"));

    [Fact]
    public void CreateServiceUnits_CreatesExpectedCatalog()
    {
        var units = DevelopmentDataScenario.CreateServiceUnits();

        Assert.Equal(4, units.Count);

        Assert.Collection(
            units.OrderBy(unit => unit.Code),
            unit =>
            {
                Assert.Equal("3D", unit.Code);
                Assert.Equal("3D tisk", unit.DisplayName);
                Assert.Equal(
                    ServiceType.ThreeDPrint,
                    unit.DefaultServiceType);
            },
            unit =>
            {
                Assert.Equal("DIL", unit.Code);
                Assert.Equal("Dílna", unit.DisplayName);
                Assert.Equal(
                    ServiceType.Workshop,
                    unit.DefaultServiceType);
            },
            unit =>
            {
                Assert.Equal("PLT", unit.Code);
                Assert.Equal("Plotr", unit.DisplayName);
                Assert.Equal(
                    ServiceType.LargeFormatPrint,
                    unit.DefaultServiceType);
            },
            unit =>
            {
                Assert.Equal("SEK", unit.Code);
                Assert.Equal("Sekretariát", unit.DisplayName);
                Assert.Equal(
                    ServiceType.Other,
                    unit.DefaultServiceType);
            });

        Assert.All(units, unit => Assert.True(unit.IsActive));
    }

    [Fact]
    public void CreateServiceUnitAssignments_IsolatesRequesterScopes()
    {
        var assignments =
            DevelopmentDataScenario.CreateServiceUnitAssignments(
                Users);

        Assert.Equal(6, assignments.Count);
        Assert.All(
            assignments,
            assignment => Assert.True(assignment.IsActive));

        Assert.Single(
            assignments,
            assignment =>
                assignment.UserId == Users.ThreeDPrintRequester &&
                assignment.ServiceUnitId ==
                    DevelopmentDataScenario.ThreeDPrintServiceUnitId);

        Assert.Single(
            assignments,
            assignment =>
                assignment.UserId == Users.WorkshopRequester &&
                assignment.ServiceUnitId ==
                    DevelopmentDataScenario.WorkshopServiceUnitId);

        Assert.Single(
            assignments,
            assignment =>
                assignment.UserId == Users.PlotterRequester &&
                assignment.ServiceUnitId ==
                    DevelopmentDataScenario.PlotterServiceUnitId);

        var secretariatAssignments = assignments
            .Where(
                assignment =>
                    assignment.ServiceUnitId ==
                    DevelopmentDataScenario.SecretariatServiceUnitId)
            .ToArray();

        Assert.Equal(3, secretariatAssignments.Length);
        Assert.Equal(
            new[]
            {
                Users.SecretariatRequesterA,
                Users.SecretariatRequesterB,
                Users.SecretariatRequesterC
            }.OrderBy(id => id),
            secretariatAssignments
                .Select(assignment => assignment.UserId)
                .OrderBy(id => id));

        Assert.DoesNotContain(
            assignments,
            assignment => assignment.UserId == Users.Administrator);
    }

    [Fact]
    public void CreateCustomerCreditAccounts_CreatesUsefulBalances()
    {
        var accounts =
            DevelopmentDataScenario.CreateCustomerCreditAccounts(
                Users);

        Assert.Equal(2, accounts.Count);

        var primaryCustomer = Assert.Single(
            accounts,
            account => account.OwnerId == Users.PrimaryCustomer);

        var lowCreditCustomer = Assert.Single(
            accounts,
            account => account.OwnerId == Users.LowCreditCustomer);

        Assert.Equal(Money.FromCrowns(1210m), primaryCustomer.Balance);
        Assert.Equal(4, primaryCustomer.Movements.Count);

        Assert.Equal(Money.FromCrowns(200m), lowCreditCustomer.Balance);
        Assert.Single(lowCreditCustomer.Movements);
    }

    [Fact]
    public void CreateJobs_CoversCustomersRequestersAndStates()
    {
        var jobs = DevelopmentDataScenario.CreateJobs(Users);

        Assert.Equal(6, jobs.Count);
        Assert.Equal(
            2,
            jobs.Select(job => job.CustomerUserId).Distinct().Count());

        Assert.Contains(
            jobs,
            job =>
                job.CustomerUserId == Users.LowCreditCustomer &&
                job.Id == DevelopmentDataScenario.PublishedUnpaidJobId &&
                job.Price == Money.FromCrowns(520m));

        Assert.Contains(
            jobs,
            job =>
                job.ServiceUnitId ==
                    DevelopmentDataScenario.SecretariatServiceUnitId &&
                job.CreatedByUserId == Users.SecretariatRequesterA &&
                job.ProductionStatus == JobProductionStatus.Draft);

        Assert.Contains(
            jobs,
            job => job.ProductionStatus == JobProductionStatus.InProduction);

        Assert.Contains(
            jobs,
            job => job.ProductionStatus == JobProductionStatus.ReadyForPickup);

        Assert.Contains(
            jobs,
            job => job.ProductionStatus == JobProductionStatus.Completed);

        Assert.Contains(
            jobs,
            job => job.ProductionStatus == JobProductionStatus.Cancelled);

        Assert.Equal(
            6,
            jobs.Select(job => job.Number).Distinct().Count());
    }

    [Fact]
    public void PaidJobs_HaveMatchingPrimaryCustomerCreditOperations()
    {
        var primaryCustomerAccount =
            DevelopmentDataScenario.CreateCustomerCreditAccounts(Users)
                .Single(
                    account =>
                        account.OwnerId == Users.PrimaryCustomer);

        var paidJobs = DevelopmentDataScenario.CreateJobs(Users)
            .Where(
                job =>
                    job.CustomerUserId == Users.PrimaryCustomer &&
                    job.PaymentStatus == JobPaymentStatus.Paid)
            .ToArray();

        Assert.Equal(3, paidJobs.Length);

        foreach (var job in paidJobs)
        {
            Assert.Equal(
                JobSettlementType.Credit,
                job.SettlementType);

            Assert.Equal(job.Id, job.SettlementReferenceId);

            Assert.Contains(
                primaryCustomerAccount.Movements,
                movement => movement.OperationId == job.Id);
        }
    }
}
