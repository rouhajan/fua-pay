using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Jobs.Web;
using FuaPay.Web.Modules.ServiceUnits.Application;

namespace FuaPay.Web.Tests.Modules.Jobs.Web;

public sealed class JobManagementPageContextResolverTests
{
    [Fact]
    public async Task ResolveAsync_RequesterWithOneUnit_UsesOnlyThatUnit()
    {
        var user = CreateUser(AccessRole.Customer, AccessRole.Requester);
        var unit = CreateUnit("3D", "3D tisk");
        var resolver = new JobManagementPageContextResolver(
            new StubServiceUnitQueries(requesterUnits: [unit]));

        var context = await resolver.ResolveAsync(
            AccessClaimsPrincipalFactory.Create(user, "test"),
            requestedView: "requester");

        Assert.NotNull(context);
        Assert.Equal(AccessView.Requester, context.ActiveView);
        Assert.Single(context.AvailableServiceUnits);
        Assert.Equal(unit.Id, context.SelectedServiceUnitId);
        Assert.True(context.Actor.CanManage(unit.Id));
        Assert.Equal(
            new[] { unit.Id },
            context.Actor.ServiceUnitIds);
    }

    [Fact]
    public async Task ResolveAsync_RequesterWithInvalidRequestedUnit_DoesNotExpandScope()
    {
        var user = CreateUser(AccessRole.Customer, AccessRole.Requester);
        var allowed = CreateUnit("3D", "3D tisk");
        var resolver = new JobManagementPageContextResolver(
            new StubServiceUnitQueries(requesterUnits: [allowed]));

        var context = await resolver.ResolveAsync(
            AccessClaimsPrincipalFactory.Create(user, "test"),
            requestedView: "requester",
            requestedServiceUnitId: Guid.NewGuid());

        Assert.NotNull(context);
        Assert.Equal(allowed.Id, context.SelectedServiceUnitId);
        Assert.Equal(new[] { allowed.Id }, context.Actor.ServiceUnitIds);
    }

    [Fact]
    public async Task ResolveAsync_AdminWithoutFilter_UsesAllScope()
    {
        var user = CreateUser(
            AccessRole.Customer,
            AccessRole.Requester,
            AccessRole.Admin);
        var resolver = new JobManagementPageContextResolver(
            new StubServiceUnitQueries(
                activeUnits: [CreateUnit("3D", "3D tisk")]));

        var context = await resolver.ResolveAsync(
            AccessClaimsPrincipalFactory.Create(user, "test"),
            requestedView: "admin");

        Assert.NotNull(context);
        Assert.True(context.IsAdministrator);
        Assert.Equal(JobManagementScope.All, context.Actor.Scope);
    }

    private static AccessUser CreateUser(params AccessRole[] roles)
    {
        var now = new DateTimeOffset(
            2026,
            7,
            29,
            15,
            0,
            0,
            TimeSpan.Zero);
        var user = new AccessUser(
            Guid.NewGuid(),
            "Testovací uživatel",
            "test@example.invalid",
            now);

        foreach (var role in roles)
        {
            user.GrantRole(
                Guid.NewGuid(),
                role,
                now,
                RoleChangeActor.ForProcess("test"));
        }

        return user;
    }

    private static ServiceUnitReadModel CreateUnit(
        string code,
        string name)
    {
        return new ServiceUnitReadModel(
            Guid.NewGuid(),
            code,
            name,
            code == "3D"
                ? ServiceType.ThreeDPrint
                : ServiceType.LargeFormatPrint);
    }

    private sealed class StubServiceUnitQueries : IServiceUnitQueries
    {
        private readonly IReadOnlyList<ServiceUnitReadModel> _activeUnits;
        private readonly IReadOnlyList<ServiceUnitReadModel> _requesterUnits;

        public StubServiceUnitQueries(
            IReadOnlyList<ServiceUnitReadModel>? activeUnits = null,
            IReadOnlyList<ServiceUnitReadModel>? requesterUnits = null)
        {
            _activeUnits = activeUnits ?? [];
            _requesterUnits = requesterUnits ?? [];
        }

        public Task<IReadOnlyList<ServiceUnitReadModel>> ListActiveAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_activeUnits);

        public Task<IReadOnlyList<ServiceUnitReadModel>> ListForRequesterAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_requesterUnits);

        public Task<ServiceUnitReadModel?> FindActiveAsync(
            Guid serviceUnitId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _activeUnits.Concat(_requesterUnits)
                    .SingleOrDefault(item => item.Id == serviceUnitId));

        public Task<IReadOnlyList<ServiceUnitAdministrationListItem>> ListAllAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RequesterServiceUnitAssignmentReadModel>>
            ListAssignmentsForUserAsync(
                Guid userId,
                bool includeRevoked = false,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
