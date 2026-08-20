using System.Security.Claims;

using FuaPay.Web.Modules.Access.Development;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Infrastructure.Entra;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Pages;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Tests.Pages;

public sealed class IndexModelTests
{
    [Fact]
    public async Task OnGetAsync_CustomerView_LoadsCustomerDashboard()
    {
        var userId = Guid.NewGuid();
        var creditQueries = new StubCreditQueries();
        var jobQueries = new StubJobQueries();
        var serviceUnitQueries = new StubServiceUnitQueries();

        var model = CreateModel(
            userId,
            [AccessRole.Customer],
            creditQueries,
            jobQueries,
            serviceUnitQueries);

        await model.OnGetAsync();

        var dashboard = Assert.IsType<CustomerDashboardModel>(
            model.CustomerDashboard);

        Assert.Equal(121_000, dashboard.BalanceMinorUnits);
        Assert.Equal(1L, dashboard.AwaitingPaymentCount);
        Assert.Equal(5L, dashboard.TotalJobCount);
        Assert.Single(dashboard.RecentCreditMovements);
        Assert.Single(dashboard.RecentJobs);
        Assert.Equal(0, serviceUnitQueries.CallCount);
    }

    [Fact]
    public async Task OnGetAsync_RequesterView_LoadsSharedUnitScope()
    {
        var userId = Guid.NewGuid();
        var serviceUnitQueries = new StubServiceUnitQueries();
        var model = CreateModel(
            userId,
            [AccessRole.Customer, AccessRole.Requester],
            new StubCreditQueries(),
            new StubJobQueries(),
            serviceUnitQueries);

        await model.OnGetAsync();

        var dashboard = Assert.IsType<RequesterDashboardModel>(
            model.RequesterDashboard);

        Assert.Equal(6L, dashboard.TotalJobCount);
        Assert.Equal(2, dashboard.ServiceUnits.Count);
        Assert.Null(dashboard.SelectedServiceUnitId);
        Assert.Equal("Všechna pracoviště", dashboard.ScopeLabel);
    }

    [Fact]
    public async Task OnGetAsync_RequesterWithSingleUnit_SelectsOnlyUnit()
    {
        var userId = Guid.NewGuid();
        var onlyUnit = new ServiceUnitReadModel(
            Guid.NewGuid(),
            "3D",
            "3D tisk",
            ServiceType.ThreeDPrint);
        var serviceUnitQueries = new StubServiceUnitQueries([onlyUnit]);
        var jobQueries = new StubJobQueries();
        var model = CreateModel(
            userId,
            [AccessRole.Customer, AccessRole.Requester],
            new StubCreditQueries(),
            jobQueries,
            serviceUnitQueries);

        await model.OnGetAsync(view: "requester");

        var dashboard = Assert.IsType<RequesterDashboardModel>(
            model.RequesterDashboard);
        Assert.Single(dashboard.ServiceUnits);
        Assert.Equal(onlyUnit.Id, dashboard.SelectedServiceUnitId);
        Assert.Equal(onlyUnit.DisplayName, dashboard.ScopeLabel);
        Assert.Equal(
            new[] { onlyUnit.Id },
            jobQueries.LastManagementActor!.ServiceUnitIds.ToArray());
    }

    [Fact]
    public async Task OnGetAsync_RequesterUnitFilter_UsesVerifiedUnit()
    {
        var userId = Guid.NewGuid();
        var serviceUnitQueries = new StubServiceUnitQueries();
        var selected = serviceUnitQueries.RequesterUnits[0];
        var jobQueries = new StubJobQueries();
        var model = CreateModel(
            userId,
            [AccessRole.Customer, AccessRole.Requester],
            new StubCreditQueries(),
            jobQueries,
            serviceUnitQueries);

        await model.OnGetAsync(
            view: "requester",
            unit: selected.Id);

        var dashboard = Assert.IsType<RequesterDashboardModel>(
            model.RequesterDashboard);

        Assert.Equal(selected.Id, dashboard.SelectedServiceUnitId);
        Assert.Equal(selected.DisplayName, dashboard.ScopeLabel);
        Assert.Equal(
            new[] { selected.Id },
            jobQueries.LastManagementActor!.ServiceUnitIds.ToArray());
    }

    [Fact]
    public async Task OnGetAsync_AdminView_LoadsGlobalDashboard()
    {
        var userId = Guid.NewGuid();
        var model = CreateModel(
            userId,
            [AccessRole.Customer, AccessRole.Admin],
            new StubCreditQueries(),
            new StubJobQueries(),
            new StubServiceUnitQueries());

        await model.OnGetAsync();

        var dashboard = Assert.IsType<AdminDashboardModel>(
            model.AdminDashboard);

        Assert.Equal(6L, dashboard.TotalJobCount);
        Assert.Equal(2, dashboard.ServiceUnits.Count);
    }

    [Fact]
    public async Task OnGetAsync_AuthenticationFailure_IsExposedWithoutDetails()
    {
        var model = CreateModel(
            Guid.NewGuid(),
            [AccessRole.Customer],
            new StubCreditQueries(),
            new StubJobQueries(),
            new StubServiceUnitQueries());

        await model.OnGetAsync(authenticationError: true);

        Assert.True(model.AuthenticationError);
    }

    private static IndexModel CreateModel(
        Guid userId,
        IReadOnlyCollection<AccessRole> roles,
        ICreditQueries creditQueries,
        IJobQueries jobQueries,
        IServiceUnitQueries serviceUnitQueries)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };

        claims.AddRange(
            roles.Select(
                role => new Claim(
                    ClaimTypes.Role,
                    role.ToString())));

        return new IndexModel(
            new DevelopmentSignInAvailability(isEnabled: true),
            new EntraAuthenticationAvailability(false, null),
            creditQueries,
            jobQueries,
            serviceUnitQueries)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            claims,
                            authenticationType: "test"))
                }
            }
        };
    }

    private sealed class StubCreditQueries : ICreditQueries
    {
        public Task<CreditAdministrationMovementPage>
            ListAdministrationMovementsAsync(
                CreditAdministrationMovementFilter filter,
                CreditMovementPageRequest page,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<CreditAccountSummary?> FindAccountForOwnerAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<CreditAccountSummary?>(
                new CreditAccountSummary(
                    Guid.NewGuid(),
                    ownerId,
                    121_000,
                    4));
        }

        public Task<CreditMovementListItem?> FindMovementForOwnerAsync(
            Guid ownerId,
            Guid operationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CreditMovementListItem?>(null);

        public Task<CreditMovementPage> ListMovementsForOwnerAsync(
            Guid ownerId,
            CreditMovementPageRequest page,
            CancellationToken cancellationToken = default)
        {
            var item = new CreditMovementListItem(
                Guid.NewGuid(),
                CreditMovementType.Credit,
                200_000,
                200_000,
                "Testovací dobití kreditu",
                DateTimeOffset.UtcNow,
                1);

            return Task.FromResult(
                new CreditMovementPage(
                    [item],
                    page.Offset,
                    page.Limit,
                    totalCount: 1));
        }
    }

    private sealed class StubServiceUnitQueries : IServiceUnitQueries
    {
        public StubServiceUnitQueries(
            IReadOnlyList<ServiceUnitReadModel>? requesterUnits = null)
        {
            RequesterUnits = requesterUnits ??
            [
                new(
                    Guid.NewGuid(),
                    "3D",
                    "3D tisk",
                    ServiceType.ThreeDPrint),
                new(
                    Guid.NewGuid(),
                    "PLT",
                    "Velkoformátový tisk",
                    ServiceType.LargeFormatPrint)
            ];
        }

        public IReadOnlyList<ServiceUnitReadModel> RequesterUnits { get; }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ServiceUnitAdministrationListItem>> ListAllAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<RequesterServiceUnitAssignmentReadModel>>
            ListAssignmentsForUserAsync(
                Guid userId,
                bool includeRevoked = false,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceUnitReadModel?> FindActiveAsync(
            Guid serviceUnitId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(
                RequesterUnits.SingleOrDefault(
                    item => item.Id == serviceUnitId));
        }

        public Task<IReadOnlyList<ServiceUnitReadModel>> ListActiveAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(RequesterUnits);
        }

        public Task<IReadOnlyList<ServiceUnitReadModel>> ListForRequesterAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(RequesterUnits);
        }
    }

    private sealed class StubJobQueries : IJobQueries
    {
        public JobManagementActor? LastManagementActor { get; private set; }

        public Task<CustomerJobSummary> GetCustomerSummaryAsync(
            Guid customerUserId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new CustomerJobSummary(5, 1));
        }

        public Task<JobPage<JobListItem>> ListForCustomerAsync(
            Guid customerUserId,
            JobListFilter filter,
            JobPageRequest page,
            CancellationToken cancellationToken = default)
        {
            var item = CreateItem(customerUserId);
            return Task.FromResult(
                new JobPage<JobListItem>(
                    [item],
                    page.Offset,
                    page.Limit,
                    1));
        }

        public Task<ManagementJobSummary> GetManagementSummaryAsync(
            JobManagementActor actor,
            CancellationToken cancellationToken = default)
        {
            LastManagementActor = actor;
            return Task.FromResult(
                new ManagementJobSummary(6, 4, 1));
        }

        public Task<JobPage<JobListItem>> ListForManagementAsync(
            JobManagementActor actor,
            JobListFilter filter,
            JobPageRequest page,
            CancellationToken cancellationToken = default)
        {
            LastManagementActor = actor;
            var item = CreateItem(Guid.NewGuid());
            return Task.FromResult(
                new JobPage<JobListItem>(
                    [item],
                    page.Offset,
                    page.Limit,
                    1));
        }

        public Task<JobDetail?> FindForCustomerAsync(
            Guid customerUserId,
            Guid jobId,
            CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

        public Task<JobDetail?> FindForManagementAsync(
            JobManagementActor actor,
            Guid jobId,
            CancellationToken cancellationToken = default) =>
                throw new NotSupportedException();

        private static JobListItem CreateItem(Guid customerUserId)
        {
            return new JobListItem(
                Guid.NewGuid(),
                NextJobNumber(),
                Guid.NewGuid(),
                customerUserId,
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                "3D tisk modelu ateliéru",
                39_000,
                JobProductionStatus.InProduction,
                JobPaymentStatus.Paid,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
        }
    }
}
