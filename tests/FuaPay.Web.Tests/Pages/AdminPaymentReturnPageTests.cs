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
        Assert.NotNull(
            typeof(IndexModel).GetMethod("OnPostPartialRefundAsync"));
        Assert.Null(
            typeof(IndexModel).GetMethod("OnGetPartialRefundAsync"));
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
            service,
            new RecordingCardTopUpSettlementReturnService());
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

    [Fact]
    public async Task ReversePost_BareInProgressRefundMessageDoesNotClaimProcessing()
    {
        var service = new RecordingCardJobSettlementReturnService(
            CardJobSettlementReturnOutcome.RequiresAttention);
        var model = new IndexModel(
            new EmptyPaymentQueries(),
            new EmptyAccessUserQueries(),
            new EmptyReconciliationQueries(),
            new EmptySettlementReturnQueries(),
            service,
            new RecordingCardTopUpSettlementReturnService());
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

        var response = await model.OnPostReverseAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Approved full return");

        Assert.IsType<RedirectToPageResult>(response);
        var message = Assert.IsType<string>(
            model.TempData["StatusMessage"]);
        Assert.DoesNotContain(
            "refund se zpracovává",
            message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "vyžaduje pozornost",
            message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PartialRefundPost_UsesAuthenticatedAdministratorAndMinorUnits()
    {
        var service = new RecordingCardJobSettlementReturnService(
            CardJobSettlementReturnOutcome.PartialRefundCompleted);
        var model = new IndexModel(
            new EmptyPaymentQueries(),
            new EmptyAccessUserQueries(),
            new EmptyReconciliationQueries(),
            new EmptySettlementReturnQueries(),
            service,
            new RecordingCardTopUpSettlementReturnService());
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
        model.PageContext = new PageContext { HttpContext = httpContext };
        model.TempData = new TempDataDictionary(
            httpContext,
            new MemoryTempDataProvider());
        var operationId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        var response = await model.OnPostPartialRefundAsync(
            operationId,
            paymentId,
            25.50m,
            "Approved partial return");

        Assert.IsType<RedirectToPageResult>(response);
        var command = Assert.IsType<CardJobPartialRefundCommand>(
            service.PartialCommand);
        Assert.Equal(operationId, command.OperationId);
        Assert.Equal(paymentId, command.OriginalPaymentId);
        Assert.Equal(administratorId, command.AdministratorUserId);
        Assert.Equal(2_550, command.AmountMinorUnits);
        Assert.Equal("Approved partial return", command.Reason);
    }

    [Fact]
    public async Task PartialRefundPost_MaxDecimalReturnsValidationWithoutCallingService()
    {
        var service = new RecordingCardJobSettlementReturnService();
        var model = new IndexModel(
            new EmptyPaymentQueries(),
            new EmptyAccessUserQueries(),
            new EmptyReconciliationQueries(),
            new EmptySettlementReturnQueries(),
            service,
            new RecordingCardTopUpSettlementReturnService())
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var response = await model.OnPostPartialRefundAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            decimal.MaxValue,
            "Overflow regression");

        Assert.IsType<PageResult>(response);
        Assert.False(model.ModelState.IsValid);
        Assert.True(model.ModelState.ContainsKey("amount"));
        Assert.Null(service.PartialCommand);
    }

    [Fact]
    public async Task CardTopUpRejectedPostReportsReleasedCreditReservation()
    {
        var topUpService = new RecordingCardTopUpSettlementReturnService(
            CardTopUpSettlementReturnOutcome.Rejected);
        var model = new IndexModel(
            new EmptyPaymentQueries(),
            new EmptyAccessUserQueries(),
            new EmptyReconciliationQueries(),
            new EmptySettlementReturnQueries(),
            new RecordingCardJobSettlementReturnService(),
            topUpService);
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
        model.PageContext = new PageContext { HttpContext = httpContext };
        model.TempData = new TempDataDictionary(
            httpContext,
            new MemoryTempDataProvider());

        var response = await model.OnPostReturnTopUpAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Rejected return");

        Assert.IsType<RedirectToPageResult>(response);
        var message = Assert.IsType<string>(model.TempData["StatusMessage"]);
        Assert.Contains("zamítnuta", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uvolněna", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nejasný", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("zůstává rezervován", message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(topUpService.Command);
    }

    private sealed class RecordingCardJobSettlementReturnService :
        ICardJobSettlementReturnService
    {
        private readonly CardJobSettlementReturnOutcome _outcome;

        public RecordingCardJobSettlementReturnService(
            CardJobSettlementReturnOutcome outcome =
                CardJobSettlementReturnOutcome.ReverseCompleted)
        {
            _outcome = outcome;
        }

        public CardJobSettlementReturnCommand? Command { get; private set; }

        public CardJobPartialRefundCommand? PartialCommand { get; private set; }

        public Task<CardJobSettlementReturnResult> ReturnAsync(
            CardJobSettlementReturnCommand command,
            CancellationToken cancellationToken = default)
        {
            Command = command;
            return Task.FromResult(new CardJobSettlementReturnResult(
                Guid.NewGuid(),
                Guid.NewGuid(),
                _outcome,
                ReverseRequestSent: true));
        }

        public Task<CardJobSettlementReturnResult> PartialRefundAsync(
            CardJobPartialRefundCommand command,
            CancellationToken cancellationToken = default)
        {
            PartialCommand = command;
            return Task.FromResult(new CardJobSettlementReturnResult(
                Guid.NewGuid(),
                Guid.NewGuid(),
                _outcome,
                ReverseRequestSent: false,
                RefundRequestSent: true));
        }
    }

    private sealed class RecordingCardTopUpSettlementReturnService :
        ICardTopUpSettlementReturnService
    {
        private readonly CardTopUpSettlementReturnOutcome _outcome;

        public RecordingCardTopUpSettlementReturnService(
            CardTopUpSettlementReturnOutcome outcome =
                CardTopUpSettlementReturnOutcome.ReverseCompleted)
        {
            _outcome = outcome;
        }

        public CardTopUpSettlementReturnCommand? Command { get; private set; }

        public Task<CardTopUpSettlementReturnResult> ReturnAsync(
            CardTopUpSettlementReturnCommand command,
            CancellationToken cancellationToken = default)
        {
            Command = command;
            return Task.FromResult(new CardTopUpSettlementReturnResult(
                Guid.NewGuid(),
                Guid.NewGuid(),
                _outcome,
                ReverseRequestSent: false,
                RefundRequestSent: false));
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
            Task.FromResult(new PaymentPage(
                [],
                page.Offset,
                page.Limit,
                TotalCount: 0));

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
            Task.FromResult<
                IReadOnlyDictionary<Guid, AccessUserOption>>(
                new Dictionary<Guid, AccessUserOption>());

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
            Task.FromResult<IReadOnlyList<PaymentReconciliationAdminItem>>([]);
    }

    private sealed class EmptySettlementReturnQueries :
        ISettlementReturnQueries
    {
        public Task<
            IReadOnlyDictionary<
                Guid,
                IReadOnlyList<SettlementReturnAdministrationItem>>>
            FindByOriginalPaymentIdsAsync(
                IEnumerable<Guid> originalPaymentIds,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<
                IReadOnlyDictionary<
                    Guid,
                    IReadOnlyList<SettlementReturnAdministrationItem>>>(
                new Dictionary<
                    Guid,
                    IReadOnlyList<SettlementReturnAdministrationItem>>());
    }
}
