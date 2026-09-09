using System.Reflection;

using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Pages.Admin.Payments;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace FuaPay.Web.Tests.Pages;

public sealed class AdminPaymentReturnPageTests
{
    [Fact]
    public void Page_RequiresAdministratorRoleAndExposesPostHandlerOnly()
    {
        var authorization = Assert.Single(
            typeof(IndexModel).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal("Admin", authorization.Roles);
        Assert.NotNull(typeof(IndexModel).GetMethod("OnPostReverseAsync"));
        Assert.Null(typeof(IndexModel).GetMethod("OnGetReverseAsync"));
    }

    [Fact]
    public async Task ReversePost_UsesAuthenticatedAdministratorAndOperationIds()
    {
        var service = new RecordingCardJobSettlementReturnService();
        var model = new IndexModel(
            new EmptyPaymentQueries(),
            new EmptyAccessUserQueries(),
            new EmptyReconciliationQueries(),
            new EmptySettlementReturnQueries(),
            service);
        var administratorId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext
        {
            User = AccessClaimsPrincipalFactory.Create(
                new AccessSessionSnapshot(
                    administratorId,
                    "Administrator",
                    "admin@example.cz",
                    AccessUserStatus.Active,
                    [AccessRole.Admin]),
                "Test")
        };
        model.PageContext = new PageContext
        {
            HttpContext = httpContext
        };
        model.TempData = new TempDataDictionary(
            httpContext,
            new MemoryTempDataProvider());
        var operationId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var response = await model.OnPostReverseAsync(
            operationId,
            paymentId,
            "Approved full return");

        var redirect = Assert.IsType<RedirectToPageResult>(response);
        Assert.Null(redirect.PageName);
        var command = Assert.IsType<CardJobSettlementReturnCommand>(
            service.Command);
        Assert.Equal(operationId, command.OperationId);
        Assert.Equal(paymentId, command.OriginalPaymentId);
        Assert.Equal(administratorId, command.AdministratorUserId);
        Assert.Equal("Approved full return", command.Reason);
        Assert.NotNull(model.TempData["StatusMessage"]);
    }

    private sealed class RecordingCardJobSettlementReturnService :
        ICardJobSettlementReturnService
    {
        public CardJobSettlementReturnCommand? Command { get; private set; }

        public Task<CardJobSettlementReturnResult> ReturnAsync(
            CardJobSettlementReturnCommand command,
            CancellationToken cancellationToken = default)
        {
            Command = command;
            return Task.FromResult(new CardJobSettlementReturnResult(
                Guid.NewGuid(),
                Guid.NewGuid(),
                CardJobSettlementReturnOutcome.Confirmed,
                ReverseRequestSent: true));
        }
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> _values =
            new Dictionary<string, object>();

        public IDictionary<string, object> LoadTempData(
            HttpContext context) =>
            _values;

        public void SaveTempData(
            HttpContext context,
            IDictionary<string, object> values)
        {
            _values = values;
        }
    }

    private sealed class EmptyPaymentQueries : IPaymentQueries
    {
        public Task<PaymentPage> ListForCustomerAsync(
            Guid customerUserId,
            PaymentListFilter filter,
            PaymentPageRequest page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentPage> ListForAdministrationAsync(
            PaymentListFilter filter,
            PaymentPageRequest page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentDetail?> FindForCustomerAsync(
            Guid customerUserId,
            Guid paymentId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentDetail?> FindForAdministrationAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyAccessUserQueries : IAccessUserQueries
    {
        public Task<AccessUserPage> ListAsync(
            AccessUserListRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AccessUserDetail?> FindDetailAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AccessUserOption>> ListActiveCustomersAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, AccessUserOption>>
            FindOptionsAsync(
                IEnumerable<Guid> userIds,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsActiveAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> IsActiveCustomerAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> CountActiveUsersWithRoleAsync(
            AccessRole role,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyReconciliationQueries :
        IPaymentReconciliationQueries
    {
        public Task<IReadOnlyList<PaymentReconciliationAdminItem>>
            ListOpenAsync(
                int limit,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptySettlementReturnQueries :
        ISettlementReturnQueries
    {
        public Task<
            IReadOnlyDictionary<Guid, SettlementReturnAdministrationItem>>
            FindByOriginalPaymentIdsAsync(
                IEnumerable<Guid> originalPaymentIds,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<
                IReadOnlyDictionary<
                    Guid,
                    SettlementReturnAdministrationItem>>(
                new Dictionary<
                    Guid,
                    SettlementReturnAdministrationItem>());
    }
}
