using FuaPay.Web.Modules.Access.Development;
using FuaPay.Web.Modules.Access.Infrastructure.Entra;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.ServiceUnits.Application;

using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FuaPay.Web.Pages;

public sealed class IndexModel : PageModel
{
    private const int DashboardItemLimit = 5;

    private readonly DevelopmentSignInAvailability
        _developmentSignIn;
    private readonly EntraAuthenticationAvailability
        _entraAuthentication;

    private readonly ICreditQueries _creditQueries;
    private readonly IJobQueries _jobQueries;
    private readonly IServiceUnitQueries _serviceUnitQueries;

    public IndexModel(
        DevelopmentSignInAvailability developmentSignIn,
        EntraAuthenticationAvailability entraAuthentication,
        ICreditQueries creditQueries,
        IJobQueries jobQueries,
        IServiceUnitQueries serviceUnitQueries)
    {
        ArgumentNullException.ThrowIfNull(developmentSignIn);
        ArgumentNullException.ThrowIfNull(entraAuthentication);
        ArgumentNullException.ThrowIfNull(creditQueries);
        ArgumentNullException.ThrowIfNull(jobQueries);
        ArgumentNullException.ThrowIfNull(serviceUnitQueries);

        _developmentSignIn = developmentSignIn;
        _entraAuthentication = entraAuthentication;
        _creditQueries = creditQueries;
        _jobQueries = jobQueries;
        _serviceUnitQueries = serviceUnitQueries;
    }

    public bool IsDevelopmentSignInEnabled =>
        _developmentSignIn.IsEnabled;

    public bool IsEntraSignInEnabled =>
        _entraAuthentication.IsEnabled;

    public bool AuthenticationError { get; private set; }

    public Guid? AccessUserId { get; private set; }

    public AccessViewSelection? ViewSelection { get; private set; }

    public CustomerDashboardModel? CustomerDashboard
    {
        get;
        private set;
    }

    public RequesterDashboardModel? RequesterDashboard
    {
        get;
        private set;
    }

    public AdminDashboardModel? AdminDashboard
    {
        get;
        private set;
    }

    public async Task OnGetAsync(
        string? view = null,
        Guid? unit = null,
        bool authenticationError = false,
        CancellationToken cancellationToken = default)
    {
        AuthenticationError = authenticationError;

        if (User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        AccessUserId = User.FindAccessUserId();
        ViewSelection = AccessViewSelector.Select(User, view);

        if (
            AccessUserId is not Guid accessUserId ||
            ViewSelection is null)
        {
            return;
        }

        switch (ViewSelection.Active.View)
        {
            case AccessView.Customer:
                await LoadCustomerDashboardAsync(
                    accessUserId,
                    cancellationToken);
                break;

            case AccessView.Requester:
                await LoadRequesterDashboardAsync(
                    accessUserId,
                    unit,
                    cancellationToken);
                break;

            case AccessView.Admin:
                await LoadAdminDashboardAsync(
                    accessUserId,
                    unit,
                    cancellationToken);
                break;
        }
    }

    private async Task LoadCustomerDashboardAsync(
        Guid customerUserId,
        CancellationToken cancellationToken)
    {
        var account =
            await _creditQueries.FindAccountForOwnerAsync(
                customerUserId,
                cancellationToken);

        var movements =
            await _creditQueries.ListMovementsForOwnerAsync(
                customerUserId,
                new CreditMovementPageRequest(
                    limit: DashboardItemLimit),
                cancellationToken);

        var jobSummary =
            await _jobQueries.GetCustomerSummaryAsync(
                customerUserId,
                cancellationToken);

        var jobs = await _jobQueries.ListForCustomerAsync(
            customerUserId,
            new JobListFilter(),
            new JobPageRequest(
                limit: DashboardItemLimit),
            cancellationToken);

        CustomerDashboard = new CustomerDashboardModel(
            account?.BalanceMinorUnits ?? 0,
            jobSummary.AwaitingPaymentCount,
            jobSummary.TotalCount,
            movements.Items,
            jobs.Items);
    }

    private async Task LoadRequesterDashboardAsync(
        Guid requesterUserId,
        Guid? requestedServiceUnitId,
        CancellationToken cancellationToken)
    {
        var serviceUnits =
            await _serviceUnitQueries.ListForRequesterAsync(
                requesterUserId,
                cancellationToken);

        var selectedServiceUnitId = SelectServiceUnit(
            requestedServiceUnitId,
            serviceUnits) ??
            (serviceUnits.Count == 1 ? serviceUnits[0].Id : null);

        var scopedServiceUnitIds = selectedServiceUnitId.HasValue
            ? new[] { selectedServiceUnitId.Value }
            : serviceUnits.Select(item => item.Id);

        var actor = new JobManagementActor(
            requesterUserId,
            scopedServiceUnitIds);

        var summary =
            await _jobQueries.GetManagementSummaryAsync(
                actor,
                cancellationToken);

        var jobs = await _jobQueries.ListForManagementAsync(
            actor,
            new JobListFilter(),
            new JobPageRequest(
                limit: DashboardItemLimit),
            cancellationToken);

        RequesterDashboard = new RequesterDashboardModel(
            summary.TotalCount,
            summary.ActiveCount,
            summary.AwaitingPaymentCount,
            jobs.Items,
            serviceUnits,
            selectedServiceUnitId);
    }

    private async Task LoadAdminDashboardAsync(
        Guid adminUserId,
        Guid? requestedServiceUnitId,
        CancellationToken cancellationToken)
    {
        var serviceUnits =
            await _serviceUnitQueries.ListActiveAsync(
                cancellationToken);

        var selectedServiceUnitId = SelectServiceUnit(
            requestedServiceUnitId,
            serviceUnits);

        var actor = selectedServiceUnitId.HasValue
            ? new JobManagementActor(
                adminUserId,
                new[] { selectedServiceUnitId.Value })
            : new JobManagementActor(
                adminUserId,
                JobManagementScope.All);

        var summary =
            await _jobQueries.GetManagementSummaryAsync(
                actor,
                cancellationToken);

        var jobs = await _jobQueries.ListForManagementAsync(
            actor,
            new JobListFilter(),
            new JobPageRequest(
                limit: DashboardItemLimit),
            cancellationToken);

        AdminDashboard = new AdminDashboardModel(
            summary.TotalCount,
            summary.ActiveCount,
            summary.AwaitingPaymentCount,
            jobs.Items,
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
