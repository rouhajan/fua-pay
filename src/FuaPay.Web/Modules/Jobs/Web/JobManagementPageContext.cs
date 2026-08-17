using System.Security.Claims;

using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.ServiceUnits.Application;

namespace FuaPay.Web.Modules.Jobs.Web;

public sealed record JobManagementPageContext(
    JobManagementActor Actor,
    AccessView ActiveView,
    string ViewKey,
    IReadOnlyList<ServiceUnitReadModel> AvailableServiceUnits,
    Guid? SelectedServiceUnitId)
{
    public bool IsAdministrator => ActiveView == AccessView.Admin;
}

public sealed class JobManagementPageContextResolver
{
    private readonly IServiceUnitQueries _serviceUnitQueries;

    public JobManagementPageContextResolver(
        IServiceUnitQueries serviceUnitQueries)
    {
        ArgumentNullException.ThrowIfNull(serviceUnitQueries);
        _serviceUnitQueries = serviceUnitQueries;
    }

    public async Task<JobManagementPageContext?> ResolveAsync(
        ClaimsPrincipal principal,
        string? requestedView,
        Guid? requestedServiceUnitId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var userId = principal.FindAccessUserId();
        var selection = AccessViewSelector.Select(
            principal,
            requestedView);

        if (
            !userId.HasValue ||
            selection is null ||
            selection.Active.View == AccessView.Customer)
        {
            return null;
        }

        IReadOnlyList<ServiceUnitReadModel> serviceUnits;
        JobManagementActor actor;
        Guid? selectedServiceUnitId = null;

        if (selection.Active.View == AccessView.Admin)
        {
            serviceUnits = await _serviceUnitQueries.ListActiveAsync(
                cancellationToken);

            selectedServiceUnitId = SelectServiceUnit(
                requestedServiceUnitId,
                serviceUnits);

            actor = selectedServiceUnitId.HasValue
                ? new JobManagementActor(
                    userId.Value,
                    new[] { selectedServiceUnitId.Value })
                : new JobManagementActor(
                    userId.Value,
                    JobManagementScope.All);
        }
        else
        {
            serviceUnits = await _serviceUnitQueries.ListForRequesterAsync(
                userId.Value,
                cancellationToken);

            selectedServiceUnitId = SelectServiceUnit(
                requestedServiceUnitId,
                serviceUnits) ??
                (serviceUnits.Count == 1 ? serviceUnits[0].Id : null);

            var scopedIds = selectedServiceUnitId.HasValue
                ? new[] { selectedServiceUnitId.Value }
                : serviceUnits.Select(item => item.Id);

            actor = new JobManagementActor(
                userId.Value,
                scopedIds);
        }

        return new JobManagementPageContext(
            actor,
            selection.Active.View,
            selection.Active.Key,
            serviceUnits,
            selectedServiceUnitId);
    }

    private static Guid? SelectServiceUnit(
        Guid? requestedServiceUnitId,
        IReadOnlyList<ServiceUnitReadModel> serviceUnits)
    {
        if (!requestedServiceUnitId.HasValue)
        {
            return null;
        }

        return serviceUnits.Any(
            item => item.Id == requestedServiceUnitId.Value)
                ? requestedServiceUnitId
                : null;
    }
}
