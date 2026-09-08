using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;

using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Receipts.Application;
using FuaPay.Web.Pages;
using FuaPay.Web.Pages.Customer.Payments;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using PaymentDetailsModel =
    FuaPay.Web.Pages.Customer.Payments.DetailsModel;

namespace FuaPay.Web.Tests.Pages;

public sealed class CustomerPaymentStage2Tests
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 9, 8, 12, 30, 0, TimeSpan.Zero);

    private static readonly Uri ProcessUri = new(
        "https://iapi.iplatebnibrana.csob.cz/api/v1.9/payment/process/" +
        "M1MIPS0000/ff41e84b7e33%40HA/20260908143000/signature");

    [Theory]
    [InlineData(PaymentPurposeType.CreditTopUp)]
    [InlineData(PaymentPurposeType.Job)]
    public void AfterCreation_FreshPaymentRedirectsToTrustedProviderUri(
        PaymentPurposeType purposeType)
    {
        var outcome = CreateOutcome(
            purposeType,
            PaymentCreationDisposition.FreshInitialization,
            ProcessUri);

        var result = CustomerPaymentNavigation.AfterCreation(outcome);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal(ProcessUri.AbsoluteUri, redirect.Url);
    }

    [Fact]
    public void AfterCreation_ResumedPaymentRedirectsToTrustedProviderUri()
    {
        var outcome = CreateOutcome(
            PaymentPurposeType.CreditTopUp,
            PaymentCreationDisposition.ResumedInitialization,
            ProcessUri);

        var result = CustomerPaymentNavigation.AfterCreation(outcome);

        Assert.Equal(
            ProcessUri.AbsoluteUri,
            Assert.IsType<RedirectResult>(result).Url);
    }

    [Theory]
    [InlineData(PaymentCreationDisposition.ExistingPayment, true)]
    [InlineData(PaymentCreationDisposition.FreshInitialization, false)]
    public void AfterCreation_WithoutRedirectDispositionUsesLocalDetails(
        PaymentCreationDisposition disposition,
        bool includeProcessUri)
    {
        var outcome = CreateOutcome(
            PaymentPurposeType.CreditTopUp,
            disposition,
            includeProcessUri ? ProcessUri : null);

        var result = CustomerPaymentNavigation.AfterCreation(outcome);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Customer/Payments/Details", redirect.PageName);
        Assert.Equal(outcome.Payment.Id, redirect.RouteValues!["id"]);
        Assert.Equal("customer", redirect.RouteValues["view"]);
    }

    [Theory]
    [InlineData("Pages/Customer/Payments/CreateTopUp.cshtml.cs")]
    [InlineData("Pages/Customer/Jobs/Details.cshtml.cs")]
    public void CustomerEntryFlow_UsesSharedTrustedNavigation(
        string relativePath)
    {
        var source = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FuaPay.Web",
                relativePath.Replace('/', Path.DirectorySeparatorChar)),
            Encoding.UTF8);

        Assert.Contains(
            "CustomerPaymentNavigation.AfterCreation",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Request.Query", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RedirectResult", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusHandler_OwnerReceivesOnlyLocalNoStoreSnapshot()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreateDetail(customerUserId, PaymentStatus.Succeeded);
        var queries = new RecordingPaymentQueries(payment);
        var provider = new RecordingProviderInitiator();
        var credits = new RecordingCreditQueries(75_000);
        var model = CreateDetailsModel(
            queries,
            provider,
            customerUserId,
            credits);

        var result = await model.OnGetStatusAsync(payment.Id);

        var json = Assert.IsType<JsonResult>(result);
        var payload = Assert.IsType<
            PaymentDetailsModel.CustomerPaymentStatusPayload>(json.Value);
        Assert.Equal(PaymentStatus.Succeeded.ToString(), payload.Status);
        Assert.Equal(PaymentDisplay.StatusLabel(payment.Status), payload.StatusLabel);
        Assert.Equal(
            PaymentDisplay.StatusCssClass(payment.Status),
            payload.StatusCssClass);
        Assert.Equal(
            DashboardDisplay.FormatDate(payment.UpdatedAt),
            payload.UpdatedAt);
        Assert.False(payload.IsPending);
        Assert.Equal(
            DashboardDisplay.FormatMoney(75_000),
            payload.AvailableCredit);
        Assert.Equal("no-store", model.Response.Headers.CacheControl);
        Assert.Equal(1, queries.FindForCustomerCalls);
        Assert.Equal(customerUserId, queries.LastCustomerUserId);
        Assert.Equal(0, provider.ResolveCalls);
        Assert.Equal(1, credits.FindForOwnerCalls);
        Assert.Equal(customerUserId, credits.LastOwnerId);

        Assert.Equal(
            [
                "AvailableCredit",
                "IsPending",
                "Status",
                "StatusCssClass",
                "StatusLabel",
                "UpdatedAt"
            ],
            payload.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .Order()
                .ToArray());
    }

    [Fact]
    public async Task StatusHandler_OtherCustomerGetsProtectedNotFound()
    {
        var ownerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var payment = CreateDetail(ownerId, PaymentStatus.Pending);
        var queries = new RecordingPaymentQueries(payment);
        var model = CreateDetailsModel(
            queries,
            new RecordingProviderInitiator(),
            otherCustomerId);

        var result = await model.OnGetStatusAsync(payment.Id);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal("no-store", model.Response.Headers.CacheControl);
        Assert.Equal(otherCustomerId, queries.LastCustomerUserId);
    }

    [Fact]
    public async Task StatusHandler_NonexistentPaymentGetsProtectedNotFound()
    {
        var customerUserId = Guid.NewGuid();
        var model = CreateDetailsModel(
            new RecordingPaymentQueries(payment: null),
            new RecordingProviderInitiator(),
            customerUserId);

        var result = await model.OnGetStatusAsync(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal("no-store", model.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task DetailsContinuation_UsesOnlyProviderValidatedProcessUri()
    {
        var customerUserId = Guid.NewGuid();
        var payment = CreateDetail(customerUserId, PaymentStatus.Pending);
        var provider = new RecordingProviderInitiator
        {
            ResolvedUri = ProcessUri
        };
        var model = CreateDetailsModel(
            new RecordingPaymentQueries(payment),
            provider,
            customerUserId);

        var result = await model.OnGetAsync(payment.Id);

        Assert.IsType<PageResult>(result);
        Assert.Equal(ProcessUri, model.TrustedProcessUri);
        Assert.Equal(1, provider.ResolveCalls);
    }

    [Fact]
    public void DetailsStatusHandler_RemainsCustomerAuthorized()
    {
        var authorize = Assert.Single(
            typeof(PaymentDetailsModel)
                .GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal("Customer", authorize.Roles);
    }

    [Fact]
    public void PollingScript_IsPendingOnlyBoundedAndStopsOnEveryFailure()
    {
        var source = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FuaPay.Web",
                "wwwroot",
                "js",
                "payment-status-polling.js"),
            Encoding.UTF8);

        Assert.Contains(
            "root.dataset.paymentStatusPending !== \"true\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "const pollIntervalMilliseconds = 2000;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "const maximumPollAttempts = 30;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "attempts >= maximumPollAttempts",
            source,
            StringComparison.Ordinal);
        Assert.Contains("if (!payload.isPending)", source, StringComparison.Ordinal);
        Assert.Contains("if (!response.ok)", source, StringComparison.Ordinal);
        Assert.Contains("catch {", source, StringComparison.Ordinal);
        Assert.Contains("redirect: \"error\"", source, StringComparison.Ordinal);
        Assert.Contains(
            "data-current-credit-balance",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailsPage_LoadsSelfHostedPollingAndKeepsManualRefresh()
    {
        var source = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                "src",
                "FuaPay.Web",
                "Pages",
                "Customer",
                "Payments",
                "Details.cshtml"),
            Encoding.UTF8);

        Assert.Contains(
            "~/js/payment-status-polling.js",
            source,
            StringComparison.Ordinal);
        Assert.Contains("data-payment-status-poller", source, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", source, StringComparison.Ordinal);
        Assert.Contains("Obnovit stav ručně", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", source, StringComparison.Ordinal);
    }

    private static PaymentCreationOutcome CreateOutcome(
        PaymentPurposeType purposeType,
        PaymentCreationDisposition disposition,
        Uri? processUri)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            Guid.NewGuid(),
            purposeType,
            purposeType == PaymentPurposeType.Job ? Guid.NewGuid() : null,
            new Money(25_000),
            PaymentProvider.Csob,
            TestTime,
            purposeType == PaymentPurposeType.CreditTopUp
                ? Guid.NewGuid()
                : null);
        payment.MarkPending("ff41e84b7e33@HA", TestTime);

        return new PaymentCreationOutcome(
            payment,
            processUri,
            disposition);
    }

    private static PaymentDetail CreateDetail(
        Guid customerUserId,
        PaymentStatus status)
    {
        return new PaymentDetail(
            Guid.NewGuid(),
            customerUserId,
            PaymentPurposeType.CreditTopUp,
            JobId: null,
            AmountMinorUnits: 25_000,
            PaymentProvider.Csob,
            status,
            ProviderReference: "ff41e84b7e33@HA",
            FailureReason: null,
            CreatedAt: TestTime.AddMinutes(-1),
            UpdatedAt: TestTime,
            CompletedAt: status == PaymentStatus.Pending ? null : TestTime,
            ProcessUri.AbsoluteUri,
            Version: 1);
    }

    private static PaymentDetailsModel CreateDetailsModel(
        IPaymentQueries queries,
        IPaymentProviderInitiator provider,
        Guid customerUserId,
        ICreditQueries? creditQueries = null)
    {
        return new PaymentDetailsModel(
            queries,
            creditQueries ?? new RecordingCreditQueries(0),
            UnusedDependency<DevelopmentPaymentService>(),
            new UnusedJobQueries(),
            UnusedDependency<PaymentCreationService>(),
            new DevelopmentPaymentAvailability(false),
            provider,
            UnusedDependency<ReceiptConfiguration>())
        {
            PageContext = CreatePageContext(customerUserId)
        };
    }

    private static PageContext CreatePageContext(Guid customerUserId)
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        customerUserId.ToString()),
                    new Claim(
                        ClaimTypes.Role,
                        AccessRole.Customer.ToString())
                ],
                authenticationType: "test"));

        return new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = principal
            }
        };
    }

    private static T UnusedDependency<T>()
        where T : class =>
        (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FuaPay.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("FuaPay.slnx was not found.");
    }

    private sealed class RecordingCreditQueries : ICreditQueries
    {
        private readonly long _balanceMinorUnits;

        public RecordingCreditQueries(long balanceMinorUnits)
        {
            _balanceMinorUnits = balanceMinorUnits;
        }

        public int FindForOwnerCalls { get; private set; }

        public Guid? LastOwnerId { get; private set; }

        public Task<CreditAccountSummary?> FindAccountForOwnerAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindForOwnerCalls++;
            LastOwnerId = ownerId;
            return Task.FromResult<CreditAccountSummary?>(new(
                Guid.NewGuid(),
                ownerId,
                _balanceMinorUnits,
                Version: 1));
        }

        public Task<CreditAdministrationMovementPage>
            ListAdministrationMovementsAsync(
                CreditAdministrationMovementFilter filter,
                CreditMovementPageRequest page,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CreditMovementListItem?> FindMovementForOwnerAsync(
            Guid ownerId,
            Guid operationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CreditMovementPage> ListMovementsForOwnerAsync(
            Guid ownerId,
            CreditMovementPageRequest page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingPaymentQueries : IPaymentQueries
    {
        private readonly PaymentDetail? _payment;

        public RecordingPaymentQueries(PaymentDetail? payment)
        {
            _payment = payment;
        }

        public int FindForCustomerCalls { get; private set; }

        public Guid? LastCustomerUserId { get; private set; }

        public Task<PaymentDetail?> FindForCustomerAsync(
            Guid customerUserId,
            Guid paymentId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindForCustomerCalls++;
            LastCustomerUserId = customerUserId;
            return Task.FromResult(
                _payment?.CustomerUserId == customerUserId &&
                _payment.Id == paymentId
                    ? _payment
                    : null);
        }

        public Task<PaymentDetail?> FindForAdministrationAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
    }

    private sealed class RecordingProviderInitiator :
        IPaymentProviderInitiator
    {
        public PaymentProvider Provider => PaymentProvider.Csob;

        public Uri? ResolvedUri { get; init; }

        public int ResolveCalls { get; private set; }

        public void EnsureAvailable() => throw new NotSupportedException();

        public Task<PaymentProviderInitializationResult> InitializeAsync(
            PaymentProviderInitializationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task VerifyAsync(
            PaymentProviderInitializationResult candidate,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Uri? ResolveTrustedProcessUri(
            PaymentProvider provider,
            string? providerReference,
            string? processUri)
        {
            ResolveCalls++;
            return ResolvedUri;
        }
    }

    private sealed class UnusedJobQueries : IJobQueries
    {
        public Task<CustomerJobSummary> GetCustomerSummaryAsync(
            Guid customerUserId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobDetail?> FindForCustomerAsync(
            Guid customerUserId,
            Guid jobId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobPage<JobListItem>> ListForCustomerAsync(
            Guid customerUserId,
            JobListFilter filter,
            JobPageRequest page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManagementJobSummary> GetManagementSummaryAsync(
            JobManagementActor actor,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobDetail?> FindForManagementAsync(
            JobManagementActor actor,
            Guid jobId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<JobPage<JobListItem>> ListForManagementAsync(
            JobManagementActor actor,
            JobListFilter filter,
            JobPageRequest page,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
