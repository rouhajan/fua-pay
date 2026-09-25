using System.Reflection;

using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;
using FuaPay.Web.Pages.Admin.Payments;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace FuaPay.Web.Tests.Modules.Payments.Infrastructure.Csob;

public sealed class CsobCardJobSettlementReturnServiceTests
{
    private const string PayId = "ff41e84b7e33@HA";

    private static readonly DateTimeOffset Now =
        new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Command_DoesNotAcceptFinancialOrProviderIdentity()
    {
        var properties = typeof(CardJobSettlementReturnCommand)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            [
                nameof(CardJobSettlementReturnCommand.AdministratorUserId),
                nameof(CardJobSettlementReturnCommand.OperationId),
                nameof(CardJobSettlementReturnCommand.OriginalPaymentId),
                nameof(CardJobSettlementReturnCommand.Reason)
            ],
            properties);
    }

    [Fact]
    public async Task ReturnAsync_PersistsInProgressBeforeSingleReverseAndConfirms()
    {
        var fixture = new Fixture();
        fixture.Gateway.OnReverse = () =>
        {
            Assert.False(fixture.Transaction.IsActive);
            Assert.Equal(
                SettlementReturnState.InProgress,
                Assert.Single(fixture.ReturnRepository.Stored).State);
            Assert.Equal(
                SettlementReturnProviderAttemptState.InProgress,
                Assert.Single(fixture.AttemptRepository.Stored).State);
        };

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.ReverseCompleted,
            result.Outcome);
        Assert.True(result.ReverseRequestSent);
        Assert.Equal(1, fixture.Gateway.ReverseCalls);
        Assert.Equal(0, fixture.Gateway.StatusCalls);
        var settlementReturn = Assert.Single(fixture.ReturnRepository.Stored);
        var attempt = Assert.Single(fixture.AttemptRepository.Stored);
        Assert.Equal(SettlementReturnState.Completed, settlementReturn.State);
        Assert.Equal(
            fixture.Payment.CustomerUserId,
            settlementReturn.CustomerUserId);
        Assert.Equal(fixture.Payment.Amount, settlementReturn.Amount);
        Assert.Equal(fixture.Payment.Id, settlementReturn.OriginalPaymentId);
        Assert.Equal(PayId, attempt.ProviderReference);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Confirmed,
            attempt.State);
        Assert.Contains(
            fixture.Audit.Entries,
            entry => entry.Action ==
                "settlement-return.card-job.reverse-started");
        Assert.Contains(
            fixture.Audit.Entries,
            entry => entry.Action ==
                "settlement-return.card-job.reverse-confirmed");
        Assert.All(
            fixture.Audit.Entries,
            entry => Assert.Equal(
                fixture.Command.AdministratorUserId,
                entry.ActorUserId));
    }

    [Fact]
    public async Task ReturnAsync_DoubleSubmitAndConfirmedReplaySendOneReverse()
    {
        var fixture = new Fixture();

        var first = await fixture.Service.ReturnAsync(fixture.Command);
        var replay = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.ReverseCompleted,
            first.Outcome);
        Assert.Equal(
            CardJobSettlementReturnOutcome.ReverseCompleted,
            replay.Outcome);
        Assert.True(first.ReverseRequestSent);
        Assert.False(replay.ReverseRequestSent);
        Assert.Equal(1, fixture.Gateway.ReverseCalls);
        Assert.Equal(0, fixture.Gateway.StatusCalls);
        Assert.Single(fixture.ReturnRepository.Stored);
        Assert.Single(fixture.AttemptRepository.Stored);
    }

    [Fact]
    public async Task ReturnAsync_AmbiguousReverseBecomesUncertainThenStatusOnly()
    {
        var fixture = new Fixture();
        fixture.Gateway.ReverseException =
            new CsobGatewayException("simulated transport ambiguity");

        var ambiguous = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RequiresAttention,
            ambiguous.Outcome);
        Assert.Equal(1, fixture.Gateway.ReverseCalls);
        Assert.Equal(
            SettlementReturnState.RequiresAttention,
            Assert.Single(fixture.ReturnRepository.Stored).State);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Uncertain,
            Assert.Single(fixture.AttemptRepository.Stored).State);

        fixture.Gateway.ReverseException = null;
        fixture.Gateway.StatusResult = Status(paymentStatus: 5);

        var recovered = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.ReverseCompleted,
            recovered.Outcome);
        Assert.False(recovered.ReverseRequestSent);
        Assert.Equal(1, fixture.Gateway.ReverseCalls);
        Assert.Equal(1, fixture.Gateway.StatusCalls);
        Assert.Equal(
            SettlementReturnState.Completed,
            Assert.Single(fixture.ReturnRepository.Stored).State);
    }

    [Fact]
    public async Task ReturnAsync_RestartWithInProgressAttemptUsesStatusOnly()
    {
        var fixture = new Fixture();
        fixture.SeedExisting(
            SettlementReturnState.InProgress,
            SettlementReturnProviderAttemptState.InProgress);
        fixture.Gateway.StatusResult = Status(paymentStatus: 5);

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.ReverseCompleted,
            result.Outcome);
        Assert.False(result.ReverseRequestSent);
        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(1, fixture.Gateway.StatusCalls);
    }

    [Fact]
    public async Task ReturnAsync_UncertainAttemptUsesStatusOnly()
    {
        var fixture = new Fixture();
        fixture.SeedExisting(
            SettlementReturnState.RequiresAttention,
            SettlementReturnProviderAttemptState.Uncertain);
        fixture.Gateway.StatusResult = Status(paymentStatus: 5);

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.ReverseCompleted,
            result.Outcome);
        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(1, fixture.Gateway.StatusCalls);
    }

    [Fact]
    public async Task AdminRecoveryPostUsesPersistedOperationForStatusOnly()
    {
        var fixture = new Fixture();
        fixture.SeedExisting(
            SettlementReturnState.RequiresAttention,
            SettlementReturnProviderAttemptState.Uncertain);
        fixture.Gateway.StatusResult = Status(paymentStatus: 5);
        var queries = DispatchProxy.Create<
            IPaymentQueries,
            UnusedQueryProxy>();
        var accessQueries = DispatchProxy.Create<
            IAccessUserQueries,
            UnusedQueryProxy>();
        var reconciliationQueries = DispatchProxy.Create<
            IPaymentReconciliationQueries,
            UnusedQueryProxy>();
        var returnQueries = DispatchProxy.Create<
            ISettlementReturnQueries,
            UnusedQueryProxy>();
        var model = new IndexModel(
            queries,
            accessQueries,
            reconciliationQueries,
            returnQueries,
            fixture.Service);
        var httpContext = new DefaultHttpContext
        {
            User = AccessClaimsPrincipalFactory.Create(
                new AccessSessionSnapshot(
                    fixture.Command.AdministratorUserId,
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
            fixture.Command.OperationId,
            fixture.Command.OriginalPaymentId,
            fixture.Command.Reason);

        Assert.IsType<RedirectToPageResult>(response);
        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(1, fixture.Gateway.StatusCalls);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Confirmed,
            Assert.Single(fixture.AttemptRepository.Stored).State);
        Assert.Equal(
            SettlementReturnState.Completed,
            Assert.Single(fixture.ReturnRepository.Stored).State);
    }

    [Fact]
    public async Task ReturnAsync_AmbiguousStatusStaysUncertainWithoutReverse()
    {
        var fixture = new Fixture();
        fixture.SeedExisting(
            SettlementReturnState.InProgress,
            SettlementReturnProviderAttemptState.InProgress);
        fixture.Gateway.StatusResult = Status(paymentStatus: 7);

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RequiresAttention,
            result.Outcome);
        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(1, fixture.Gateway.StatusCalls);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Uncertain,
            Assert.Single(fixture.AttemptRepository.Stored).State);
        Assert.Equal(
            SettlementReturnState.RequiresAttention,
            Assert.Single(fixture.ReturnRepository.Stored).State);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public async Task ReturnAsync_PreExistingProviderRefundSendsNoRefund(
        int paymentStatus)
    {
        var fixture = new Fixture();
        fixture.SeedExisting(
            SettlementReturnState.InProgress,
            SettlementReturnProviderAttemptState.InProgress);
        fixture.Gateway.StatusResult = Status(paymentStatus);

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.PreExistingProviderRefund,
            result.Outcome);
        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(1, fixture.Gateway.StatusCalls);
        Assert.Equal(
            SettlementReturnState.RequiresAttention,
            Assert.Single(fixture.ReturnRepository.Stored).State);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Rejected,
            Assert.Single(fixture.AttemptRepository.Stored).State);
        Assert.Equal(0, fixture.Gateway.RefundCalls);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public async Task ReturnAsync_DirectPreExistingProviderRefundSendsNoRefund(
        int paymentStatus)
    {
        var fixture = new Fixture();
        fixture.Gateway.ReverseResult = Reverse(
            paymentStatus,
            resultCode: 150);

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.PreExistingProviderRefund,
            result.Outcome);
        Assert.Equal(1, fixture.Gateway.ReverseCalls);
        Assert.Equal(
            SettlementReturnState.RequiresAttention,
            Assert.Single(fixture.ReturnRepository.Stored).State);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Rejected,
            Assert.Single(fixture.AttemptRepository.Stored).State);
        Assert.Equal(0, fixture.Gateway.RefundCalls);
    }

    [Fact]
    public async Task ReturnAsync_RejectedReplayDoesNotCallGatewayAgain()
    {
        var fixture = new Fixture();
        fixture.Gateway.ReverseResult = Reverse(
            paymentStatus: 9,
            resultCode: 150);

        var first = await fixture.Service.ReturnAsync(fixture.Command);
        var replay = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.PreExistingProviderRefund,
            first.Outcome);
        Assert.Equal(
            CardJobSettlementReturnOutcome.PreExistingProviderRefund,
            replay.Outcome);
        Assert.True(first.ReverseRequestSent);
        Assert.False(replay.ReverseRequestSent);
        Assert.Equal(1, fixture.Gateway.ReverseCalls);
        Assert.Equal(0, fixture.Gateway.StatusCalls);
        Assert.Equal(0, fixture.Gateway.RefundCalls);
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(0, 8)]
    [InlineData(130, 5)]
    [InlineData(150, 4)]
    [InlineData(150, 5)]
    [InlineData(160, 4)]
    [InlineData(160, 8)]
    [InlineData(130, 8)]
    [InlineData(180, 10)]
    public async Task ReturnAsync_UnexpectedSignedReverseResponseIsUncertain(
        int resultCode,
        int paymentStatus)
    {
        var fixture = new Fixture();
        fixture.Gateway.ReverseResult = Reverse(
            paymentStatus,
            resultCode);

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RequiresAttention,
            result.Outcome);
        Assert.Equal(1, fixture.Gateway.ReverseCalls);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Uncertain,
            Assert.Single(fixture.AttemptRepository.Stored).State);
    }

    [Fact]
    public async Task ReturnAsync_CancellationAfterEligibilityNeverResends()
    {
        var fixture = new Fixture();
        fixture.Gateway.ReverseException = new TaskCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.ReturnAsync(fixture.Command));

        Assert.Equal(1, fixture.Gateway.ReverseCalls);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Uncertain,
            Assert.Single(fixture.AttemptRepository.Stored).State);
        Assert.Equal(
            SettlementReturnState.RequiresAttention,
            Assert.Single(fixture.ReturnRepository.Stored).State);

        fixture.Gateway.ReverseException = null;
        fixture.Gateway.StatusResult = Status(paymentStatus: 7);
        await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(1, fixture.Gateway.ReverseCalls);
        Assert.Equal(1, fixture.Gateway.StatusCalls);
    }

    [Fact]
    public async Task ReturnAsync_PostResponsePersistenceFailureNeverResends()
    {
        var fixture = new Fixture();
        fixture.Transaction.FailOnExecution = 2;

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RequiresAttention,
            result.Outcome);
        Assert.Equal(1, fixture.Gateway.ReverseCalls);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Uncertain,
            Assert.Single(fixture.AttemptRepository.Stored).State);
        Assert.Equal(
            SettlementReturnState.RequiresAttention,
            Assert.Single(fixture.ReturnRepository.Stored).State);

        fixture.Gateway.StatusResult = Status(paymentStatus: 5);
        var recovered = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.ReverseCompleted,
            recovered.Outcome);
        Assert.Equal(1, fixture.Gateway.ReverseCalls);
        Assert.Equal(1, fixture.Gateway.StatusCalls);
    }

    [Fact]
    public async Task ReturnAsync_SettledReverseCreatesDurableFullRefundBeforePut()
    {
        var fixture = new Fixture();
        fixture.Gateway.ReverseResult = Reverse(
            paymentStatus: 8,
            resultCode: 150);
        fixture.Gateway.OnRefund = () =>
        {
            Assert.False(fixture.Transaction.IsActive);
            Assert.Equal(2, fixture.AttemptRepository.Stored.Count);
            Assert.Equal(
                SettlementReturnProviderAttemptState.Rejected,
                fixture.AttemptRepository.Stored.Single(attempt =>
                    attempt.Operation ==
                        SettlementReturnProviderOperation.Reverse).State);
            Assert.Equal(
                SettlementReturnProviderAttemptState.InProgress,
                fixture.AttemptRepository.Stored.Single(attempt =>
                    attempt.Operation ==
                        SettlementReturnProviderOperation.Refund).State);
        };

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RefundCompleted,
            result.Outcome);
        Assert.True(result.ReverseRequestSent);
        Assert.True(result.RefundRequestSent);
        Assert.Equal(1, fixture.Gateway.ReverseCalls);
        Assert.Equal(1, fixture.Gateway.RefundCalls);
        Assert.Null(fixture.Gateway.LastRefundAmountMinorUnits);
        Assert.Equal(
            SettlementReturnState.Completed,
            Assert.Single(fixture.ReturnRepository.Stored).State);
        var attempts = fixture.AttemptRepository.Stored;
        Assert.Equal(2, attempts.Count);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Confirmed,
            attempts.Single(attempt =>
                attempt.Operation ==
                    SettlementReturnProviderOperation.Refund).State);
    }

    [Fact]
    public async Task ReturnAsync_StatusEightRecoveryTransitionsToRefund()
    {
        var fixture = new Fixture();
        fixture.SeedExisting(
            SettlementReturnState.InProgress,
            SettlementReturnProviderAttemptState.InProgress);
        fixture.Gateway.StatusResult = Status(paymentStatus: 8);

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RefundCompleted,
            result.Outcome);
        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(1, fixture.Gateway.StatusCalls);
        Assert.Equal(1, fixture.Gateway.RefundCalls);
        Assert.False(result.ReverseRequestSent);
        Assert.True(result.RefundRequestSent);
    }

    [Fact]
    public async Task ReturnAsync_DirectRefundProcessingStaysUnresolved()
    {
        var fixture = new Fixture();
        fixture.Gateway.ReverseResult = Reverse(
            paymentStatus: 8,
            resultCode: 150);
        fixture.Gateway.RefundResult = Refund(paymentStatus: 9);

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RefundProcessing,
            result.Outcome);
        Assert.Equal(
            SettlementReturnState.InProgress,
            Assert.Single(fixture.ReturnRepository.Stored).State);
        Assert.Equal(
            SettlementReturnProviderAttemptState.InProgress,
            fixture.AttemptRepository.Stored.Single(attempt =>
                attempt.Operation ==
                    SettlementReturnProviderOperation.Refund).State);
        Assert.Equal(1, fixture.Gateway.RefundCalls);
    }

    [Theory]
    [InlineData(130, 10)]
    [InlineData(150, 8)]
    [InlineData(160, 10)]
    [InlineData(0, 8)]
    [InlineData(0, 7)]
    public async Task ReturnAsync_UnsafeRefundResponseFailsClosed(
        int resultCode,
        int paymentStatus)
    {
        var fixture = new Fixture();
        fixture.Gateway.ReverseResult = Reverse(
            paymentStatus: 8,
            resultCode: 150);
        fixture.Gateway.RefundResult = Refund(
            paymentStatus,
            resultCode);

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RequiresAttention,
            result.Outcome);
        Assert.Equal(1, fixture.Gateway.RefundCalls);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Uncertain,
            fixture.AttemptRepository.Stored.Single(attempt =>
                attempt.Operation ==
                    SettlementReturnProviderOperation.Refund).State);
    }

    [Theory]
    [InlineData("network failure")]
    [InlineData("timeout")]
    [InlineData("invalid signature")]
    [InlineData("tampered response")]
    [InlineData("malformed response")]
    [InlineData("stale response")]
    [InlineData("future response")]
    [InlineData("wrong payId")]
    public async Task ReturnAsync_AmbiguousRefundPutIsNeverRepeated(
        string ambiguity)
    {
        var fixture = new Fixture();
        fixture.Gateway.ReverseResult = Reverse(
            paymentStatus: 8,
            resultCode: 150);
        fixture.Gateway.RefundException = ambiguity switch
        {
            "network failure" =>
                new HttpRequestException("simulated network failure"),
            "timeout" => new TimeoutException("simulated timeout"),
            _ => new CsobGatewayException($"simulated {ambiguity}")
        };

        var ambiguous = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RequiresAttention,
            ambiguous.Outcome);
        Assert.Equal(1, fixture.Gateway.RefundCalls);

        fixture.Gateway.RefundException = null;
        fixture.Gateway.StatusResult = Status(paymentStatus: 9);
        var replay = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RefundProcessing,
            replay.Outcome);
        Assert.Equal(1, fixture.Gateway.RefundCalls);
        Assert.Equal(1, fixture.Gateway.StatusCalls);
    }

    [Fact]
    public async Task ReturnAsync_RefundCancellationIsUncertainAndNeverRepeated()
    {
        var fixture = new Fixture();
        fixture.Gateway.ReverseResult = Reverse(
            paymentStatus: 8,
            resultCode: 150);
        fixture.Gateway.RefundException = new TaskCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.ReturnAsync(fixture.Command));

        Assert.Equal(1, fixture.Gateway.RefundCalls);
        fixture.Gateway.RefundException = null;
        fixture.Gateway.StatusResult = Status(paymentStatus: 8);
        _ = await fixture.Service.ReturnAsync(fixture.Command);
        Assert.Equal(1, fixture.Gateway.RefundCalls);
        Assert.Equal(1, fixture.Gateway.StatusCalls);
    }

    [Fact]
    public async Task ReturnAsync_RefundConfirmationPersistenceFailureNeverResends()
    {
        var fixture = new Fixture();
        fixture.Gateway.ReverseResult = Reverse(
            paymentStatus: 8,
            resultCode: 150);
        fixture.Transaction.FailOnExecution = 3;

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RequiresAttention,
            result.Outcome);
        Assert.Equal(1, fixture.Gateway.RefundCalls);
        Assert.Equal(
            SettlementReturnProviderAttemptState.Uncertain,
            fixture.AttemptRepository.Stored.Single(attempt =>
                attempt.Operation ==
                    SettlementReturnProviderOperation.Refund).State);

        fixture.Gateway.StatusResult = Status(paymentStatus: 9);
        var replay = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RefundProcessing,
            replay.Outcome);
        Assert.Equal(1, fixture.Gateway.RefundCalls);
        Assert.Equal(1, fixture.Gateway.StatusCalls);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(7)]
    public async Task ReturnAsync_ActiveRefundRecoveryNeverSendsAnotherPut(
        int paymentStatus)
    {
        var fixture = new Fixture();
        fixture.SeedRefundAttempt(
            SettlementReturnState.RequiresAttention,
            SettlementReturnProviderAttemptState.Uncertain);
        fixture.Gateway.StatusResult = Status(paymentStatus);

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RequiresAttention,
            result.Outcome);
        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(0, fixture.Gateway.RefundCalls);
        Assert.Equal(1, fixture.Gateway.StatusCalls);
    }

    [Fact]
    public async Task ReturnAsync_CompletedRefundReplayPerformsNoHttp()
    {
        var fixture = new Fixture();
        fixture.Gateway.ReverseResult = Reverse(
            paymentStatus: 8,
            resultCode: 150);

        var first = await fixture.Service.ReturnAsync(fixture.Command);
        var replay = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RefundCompleted,
            first.Outcome);
        Assert.Equal(
            CardJobSettlementReturnOutcome.RefundCompleted,
            replay.Outcome);
        Assert.Equal(1, fixture.Gateway.ReverseCalls);
        Assert.Equal(1, fixture.Gateway.RefundCalls);
        Assert.Equal(0, fixture.Gateway.StatusCalls);
        Assert.False(replay.ReverseRequestSent);
        Assert.False(replay.RefundRequestSent);
    }

    [Fact]
    public async Task ReturnAsync_OrphanRefundHistoryFailsClosedWithoutHttp()
    {
        var fixture = new Fixture();
        fixture.SeedOrphanRefundHistory();

        await Assert.ThrowsAsync<
            CardJobSettlementReturnStateInconsistentException>(
                () => fixture.Service.ReturnAsync(fixture.Command));

        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(0, fixture.Gateway.StatusCalls);
        Assert.Equal(0, fixture.Gateway.RefundCalls);
    }

    [Fact]
    public async Task ReturnAsync_RefundBesideNonRejectedReverseFailsClosed()
    {
        var fixture = new Fixture();
        fixture.SeedRefundBesideNonRejectedReverse();

        await Assert.ThrowsAsync<
            CardJobSettlementReturnStateInconsistentException>(
                () => fixture.Service.ReturnAsync(fixture.Command));

        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(0, fixture.Gateway.StatusCalls);
        Assert.Equal(0, fixture.Gateway.RefundCalls);
    }

    [Fact]
    public async Task ReturnAsync_MultipleRefundAttemptsFailClosedWithoutHttp()
    {
        var fixture = new Fixture();
        fixture.SeedMultipleRefundHistory();

        await Assert.ThrowsAsync<
            CardJobSettlementReturnStateInconsistentException>(
                () => fixture.Service.ReturnAsync(fixture.Command));

        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(0, fixture.Gateway.StatusCalls);
        Assert.Equal(0, fixture.Gateway.RefundCalls);
    }

    [Fact]
    public async Task ReturnAsync_ValidRejectedReverseWithRefundRecoversStatusOnly()
    {
        var fixture = new Fixture();
        fixture.SeedRefundAttempt(
            SettlementReturnState.RequiresAttention,
            SettlementReturnProviderAttemptState.Uncertain);
        fixture.Gateway.StatusResult = Status(paymentStatus: 9);

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RefundProcessing,
            result.Outcome);
        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(1, fixture.Gateway.StatusCalls);
        Assert.Equal(0, fixture.Gateway.RefundCalls);
    }

    [Theory]
    [InlineData(
        CsobCardJobSettlementReturnService
            .ReverseFoundRefundProcessingDiagnostic)]
    [InlineData(
        CsobCardJobSettlementReturnService
            .ReverseFoundReturnedDiagnostic)]
    public async Task ReturnAsync_PreExistingReverseWithPreparedRefundFailsClosed(
        string reverseDiagnostic)
    {
        var fixture = new Fixture();
        fixture.SeedPreparedRefundHistory(
            reverseDiagnostic,
            beginReverse: true);

        await Assert.ThrowsAsync<
            CardJobSettlementReturnStateInconsistentException>(
                () => fixture.Service.ReturnAsync(fixture.Command));

        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(0, fixture.Gateway.StatusCalls);
        Assert.Equal(0, fixture.Gateway.RefundCalls);
    }

    [Fact]
    public async Task ReturnAsync_UnsentRejectedReverseWithPreparedRefundFailsClosed()
    {
        var fixture = new Fixture();
        fixture.SeedPreparedRefundHistory(
            CsobCardJobSettlementReturnService.ReverseSettledDiagnostic,
            beginReverse: false);

        await Assert.ThrowsAsync<
            CardJobSettlementReturnStateInconsistentException>(
                () => fixture.Service.ReturnAsync(fixture.Command));

        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(0, fixture.Gateway.StatusCalls);
        Assert.Equal(0, fixture.Gateway.RefundCalls);
    }

    [Fact]
    public async Task ReturnAsync_RefundPredatingReverseRejectionFailsClosed()
    {
        var fixture = new Fixture();
        fixture.SeedPreparedRefundHistory(
            CsobCardJobSettlementReturnService.ReverseSettledDiagnostic,
            beginReverse: true,
            refundPredatesRejection: true);

        await Assert.ThrowsAsync<
            CardJobSettlementReturnStateInconsistentException>(
                () => fixture.Service.ReturnAsync(fixture.Command));

        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(0, fixture.Gateway.StatusCalls);
        Assert.Equal(0, fixture.Gateway.RefundCalls);
    }

    [Fact]
    public async Task ReturnAsync_ValidSettledReverseWithPreparedRefundSendsRefund()
    {
        var fixture = new Fixture();
        fixture.SeedPreparedRefundHistory(
            CsobCardJobSettlementReturnService.ReverseSettledDiagnostic,
            beginReverse: true);

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RefundCompleted,
            result.Outcome);
        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(0, fixture.Gateway.StatusCalls);
        Assert.Equal(1, fixture.Gateway.RefundCalls);
    }

    [Fact]
    public async Task ReturnAsync_BareConcurrentRefundDoesNotClaimProcessing()
    {
        var fixture = new Fixture();
        fixture.Gateway.OnReverse =
            fixture.ConcurrentlyTransitionToBareRefundInProgress;
        fixture.Gateway.ReverseResult = Reverse(
            paymentStatus: 9,
            resultCode: 150);

        var result = await fixture.Service.ReturnAsync(fixture.Command);

        Assert.Equal(
            CardJobSettlementReturnOutcome.RequiresAttention,
            result.Outcome);
        Assert.NotEqual(
            CardJobSettlementReturnOutcome.RefundProcessing,
            result.Outcome);
        Assert.Equal(1, fixture.Gateway.ReverseCalls);
        Assert.Equal(0, fixture.Gateway.StatusCalls);
        Assert.Equal(0, fixture.Gateway.RefundCalls);
    }

    [Fact]
    public async Task ReturnAsync_RejectsUnsupportedCardTopUpWithoutGatewayCall()
    {
        var fixture = new Fixture(paymentPurpose: PaymentPurposeType.CreditTopUp);

        await Assert.ThrowsAsync<CardJobSettlementReturnNotAllowedException>(
            () => fixture.Service.ReturnAsync(fixture.Command));

        Assert.Equal(0, fixture.Gateway.ReverseCalls);
        Assert.Equal(0, fixture.Gateway.StatusCalls);
        Assert.Empty(fixture.ReturnRepository.Stored);
        Assert.Empty(fixture.AttemptRepository.Stored);
    }

    private static CsobPaymentReverseResult Reverse(
        int paymentStatus,
        int resultCode = 0) =>
        new(
            PayId,
            resultCode,
            resultCode == 0 ? "OK" : "Not reversible",
            paymentStatus,
            StatusDetail: null);

    private static CsobPaymentStatusResult Status(
        int paymentStatus,
        int resultCode = 0) =>
        new(
            PayId,
            resultCode,
            resultCode == 0 ? "OK" : "Status unavailable",
            paymentStatus,
            AuthCode: null,
            StatusDetail: null);

    private static CsobPaymentRefundResult Refund(
        int paymentStatus,
        int resultCode = 0) =>
        new(
            PayId,
            resultCode,
            resultCode == 0 ? "OK" : "Refund unresolved",
            paymentStatus,
            AuthCode: null,
            StatusDetail: null);

    private sealed class Fixture
    {
        public Fixture(
            PaymentPurposeType paymentPurpose = PaymentPurposeType.Job)
        {
            var customerId = Guid.NewGuid();
            var jobId = paymentPurpose == PaymentPurposeType.Job
                ? Guid.NewGuid()
                : (Guid?)null;
            Payment = new Payment(
                Guid.NewGuid(),
                customerId,
                paymentPurpose,
                jobId,
                new Money(12_500),
                PaymentProvider.Csob,
                Now.AddMinutes(-10),
                paymentPurpose == PaymentPurposeType.CreditTopUp
                    ? Guid.NewGuid()
                    : null);
            Payment.MarkPending(PayId, Now.AddMinutes(-9));
            Payment.Complete(Now.AddMinutes(-8));

            Job = CreateJob(customerId, jobId ?? Guid.NewGuid());

            if (paymentPurpose == PaymentPurposeType.Job)
            {
                Job.ConfirmSettlement(
                    JobSettlementType.DirectPayment,
                    Payment.Id,
                    Now.AddMinutes(-8));
            }

            PaymentRepository = new FakePaymentRepository(Payment);
            JobRepository = new FakeJobRepository(Job);
            ReturnRepository = new FakeSettlementReturnRepository();
            AttemptRepository =
                new FakeSettlementReturnProviderAttemptRepository();
            Transaction = new RecordingTransaction();
            Audit = new RecordingAuditTrail();
            Gateway = new FakeGateway();
            var timeProvider = new FixedTimeProvider(Now);
            AttemptService = new SettlementReturnProviderAttemptService(
                AttemptRepository,
                ReturnRepository,
                PaymentRepository,
                timeProvider);
            Service = new CsobCardJobSettlementReturnService(
                JobRepository,
                new ExistingJobCoordination(),
                PaymentRepository,
                ReturnRepository,
                AttemptRepository,
                new SettlementReturnRegistrationService(ReturnRepository),
                AttemptService,
                Transaction,
                Audit,
                Gateway,
                timeProvider);
            Command = new CardJobSettlementReturnCommand(
                Guid.NewGuid(),
                Payment.Id,
                Guid.NewGuid(),
                "Administrator approved full CardJob return");
        }

        public Payment Payment { get; }

        public Job Job { get; }

        public FakePaymentRepository PaymentRepository { get; }

        public FakeJobRepository JobRepository { get; }

        public FakeSettlementReturnRepository ReturnRepository { get; }

        public FakeSettlementReturnProviderAttemptRepository
            AttemptRepository
        { get; }

        public SettlementReturnProviderAttemptService AttemptService
        {
            get;
        }

        public RecordingTransaction Transaction { get; }

        public RecordingAuditTrail Audit { get; }

        public FakeGateway Gateway { get; }

        public CsobCardJobSettlementReturnService Service { get; }

        public CardJobSettlementReturnCommand Command { get; }

        public void SeedExisting(
            SettlementReturnState returnState,
            SettlementReturnProviderAttemptState attemptState)
        {
            var settlementReturn = new SettlementReturn(
                Guid.NewGuid(),
                Command.OperationId,
                SettlementReturnKind.CardJob,
                Payment.Id,
                Job.Id,
                Payment.CustomerUserId,
                Command.AdministratorUserId,
                Payment.Amount,
                Command.Reason,
                Now.AddMinutes(-5));
            settlementReturn.Begin(Now.AddMinutes(-4));

            if (returnState == SettlementReturnState.RequiresAttention)
            {
                settlementReturn.RequireAttention(Now.AddMinutes(-3));
            }
            else if (returnState != SettlementReturnState.InProgress)
            {
                throw new ArgumentOutOfRangeException(nameof(returnState));
            }

            ReturnRepository.Stored.Add(settlementReturn);

            var attempt = new SettlementReturnProviderAttempt(
                Command.OperationId,
                settlementReturn.Id,
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Reverse,
                PayId,
                Now.AddMinutes(-5));
            attempt.Begin(Now.AddMinutes(-4));

            if (
                attemptState ==
                    SettlementReturnProviderAttemptState.Uncertain)
            {
                attempt.MarkUncertain(
                    "simulated restart ambiguity",
                    Now.AddMinutes(-3));
            }
            else if (
                attemptState !=
                    SettlementReturnProviderAttemptState.InProgress)
            {
                throw new ArgumentOutOfRangeException(nameof(attemptState));
            }

            AttemptRepository.Stored.Add(attempt);
        }

        public SettlementReturnProviderAttempt SeedRefundAttempt(
            SettlementReturnState returnState,
            SettlementReturnProviderAttemptState refundState)
        {
            var settlementReturn = new SettlementReturn(
                Guid.NewGuid(),
                Command.OperationId,
                SettlementReturnKind.CardJob,
                Payment.Id,
                Job.Id,
                Payment.CustomerUserId,
                Command.AdministratorUserId,
                Payment.Amount,
                Command.Reason,
                Now.AddMinutes(-6));
            settlementReturn.Begin(Now.AddMinutes(-5));

            var reverse = new SettlementReturnProviderAttempt(
                Command.OperationId,
                settlementReturn.Id,
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Reverse,
                PayId,
                Now.AddMinutes(-6));
            reverse.Begin(Now.AddMinutes(-5));
            reverse.Reject(
                CsobCardJobSettlementReturnService.ReverseSettledDiagnostic,
                Now.AddMinutes(-4));

            var refund = new SettlementReturnProviderAttempt(
                Guid.NewGuid(),
                settlementReturn.Id,
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Refund,
                PayId,
                Now.AddMinutes(-4));
            refund.Begin(Now.AddMinutes(-3));

            if (
                refundState ==
                    SettlementReturnProviderAttemptState.Uncertain)
            {
                refund.MarkUncertain(
                    "simulated refund ambiguity",
                    Now.AddMinutes(-2));
            }
            else if (
                refundState !=
                    SettlementReturnProviderAttemptState.InProgress)
            {
                throw new ArgumentOutOfRangeException(nameof(refundState));
            }

            if (returnState == SettlementReturnState.RequiresAttention)
            {
                settlementReturn.RequireAttention(Now.AddMinutes(-2));
            }
            else if (returnState != SettlementReturnState.InProgress)
            {
                throw new ArgumentOutOfRangeException(nameof(returnState));
            }

            ReturnRepository.Stored.Add(settlementReturn);
            AttemptRepository.Stored.Add(reverse);
            AttemptRepository.Stored.Add(refund);
            return refund;
        }

        public void SeedPreparedRefundHistory(
            string reverseDiagnostic,
            bool beginReverse,
            bool refundPredatesRejection = false)
        {
            var settlementReturn = CreateInProgressReturn(
                requestedAt: Now.AddMinutes(-7),
                startedAt: Now.AddMinutes(-6));
            var reverse = new SettlementReturnProviderAttempt(
                Command.OperationId,
                settlementReturn.Id,
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Reverse,
                PayId,
                Now.AddMinutes(-7));

            if (beginReverse)
            {
                reverse.Begin(Now.AddMinutes(-6));
            }

            reverse.Reject(reverseDiagnostic, Now.AddMinutes(-4));

            var refund = new SettlementReturnProviderAttempt(
                Guid.NewGuid(),
                settlementReturn.Id,
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Refund,
                PayId,
                refundPredatesRejection
                    ? Now.AddMinutes(-5)
                    : Now.AddMinutes(-4));

            ReturnRepository.Stored.Add(settlementReturn);
            AttemptRepository.Stored.Add(reverse);
            AttemptRepository.Stored.Add(refund);
        }

        public void ConcurrentlyTransitionToBareRefundInProgress()
        {
            var settlementReturn = Assert.Single(ReturnRepository.Stored);
            var reverse = Assert.Single(AttemptRepository.Stored);
            reverse.Reject(
                CsobCardJobSettlementReturnService.ReverseSettledDiagnostic,
                Now);
            var refund = new SettlementReturnProviderAttempt(
                Guid.NewGuid(),
                settlementReturn.Id,
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Refund,
                PayId,
                Now);
            refund.Begin(Now);
            AttemptRepository.Stored.Add(refund);
        }

        public void SeedOrphanRefundHistory()
        {
            var settlementReturn = CreateInProgressReturn(
                requestedAt: Now.AddMinutes(-6),
                startedAt: Now.AddMinutes(-5));
            var refund = new SettlementReturnProviderAttempt(
                Guid.NewGuid(),
                settlementReturn.Id,
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Refund,
                PayId,
                Now.AddMinutes(-4));
            refund.Begin(Now.AddMinutes(-3));

            ReturnRepository.Stored.Add(settlementReturn);
            AttemptRepository.Stored.Add(refund);
        }

        public void SeedRefundBesideNonRejectedReverse()
        {
            var settlementReturn = CreateInProgressReturn(
                requestedAt: Now.AddMinutes(-7),
                startedAt: Now.AddMinutes(-6));
            var reverse = new SettlementReturnProviderAttempt(
                Command.OperationId,
                settlementReturn.Id,
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Reverse,
                PayId,
                Now.AddMinutes(-7));
            reverse.Begin(Now.AddMinutes(-6));

            var refund = new SettlementReturnProviderAttempt(
                Guid.NewGuid(),
                settlementReturn.Id,
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Refund,
                PayId,
                Now.AddMinutes(-5));
            refund.Reject(
                "simulated invalid Refund beside active Reverse",
                Now.AddMinutes(-4));

            ReturnRepository.Stored.Add(settlementReturn);
            AttemptRepository.Stored.Add(reverse);
            AttemptRepository.Stored.Add(refund);
        }

        public void SeedMultipleRefundHistory()
        {
            var settlementReturn = CreateInProgressReturn(
                requestedAt: Now.AddMinutes(-8),
                startedAt: Now.AddMinutes(-7));
            var reverse = new SettlementReturnProviderAttempt(
                Command.OperationId,
                settlementReturn.Id,
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Reverse,
                PayId,
                Now.AddMinutes(-8));
            reverse.Begin(Now.AddMinutes(-7));
            reverse.Reject(
                CsobCardJobSettlementReturnService.ReverseSettledDiagnostic,
                Now.AddMinutes(-6));

            var firstRefund = new SettlementReturnProviderAttempt(
                Guid.NewGuid(),
                settlementReturn.Id,
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Refund,
                PayId,
                Now.AddMinutes(-6));
            firstRefund.Reject(
                "simulated historical duplicate Refund",
                Now.AddMinutes(-5));

            var secondRefund = new SettlementReturnProviderAttempt(
                Guid.NewGuid(),
                settlementReturn.Id,
                PaymentProvider.Csob,
                SettlementReturnProviderOperation.Refund,
                PayId,
                Now.AddMinutes(-5));
            secondRefund.Begin(Now.AddMinutes(-4));

            ReturnRepository.Stored.Add(settlementReturn);
            AttemptRepository.Stored.Add(reverse);
            AttemptRepository.Stored.Add(firstRefund);
            AttemptRepository.Stored.Add(secondRefund);
        }

        private SettlementReturn CreateInProgressReturn(
            DateTimeOffset requestedAt,
            DateTimeOffset startedAt)
        {
            var settlementReturn = new SettlementReturn(
                Guid.NewGuid(),
                Command.OperationId,
                SettlementReturnKind.CardJob,
                Payment.Id,
                Job.Id,
                Payment.CustomerUserId,
                Command.AdministratorUserId,
                Payment.Amount,
                Command.Reason,
                requestedAt);
            settlementReturn.Begin(startedAt);
            return settlementReturn;
        }

        private static Job CreateJob(Guid customerId, Guid jobId)
        {
            var job = new Job(
                jobId,
                "TEST-2026-000001",
                Guid.NewGuid(),
                customerId,
                Guid.NewGuid(),
                ServiceType.ThreeDPrint,
                "CardJob return",
                "Job used to test CSOB payment reverse orchestration",
                new Money(12_500),
                Now.AddMinutes(-12));
            job.Publish(Now.AddMinutes(-11));
            return job;
        }
    }

    private sealed class RecordingTransaction : IApplicationTransaction
    {
        private int _executions;

        public int? FailOnExecution { get; set; }

        public bool IsActive { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            var execution = Interlocked.Increment(ref _executions);

            if (FailOnExecution == execution)
            {
                throw new InvalidOperationException(
                    "simulated post-response persistence failure");
            }

            Assert.False(IsActive);
            IsActive = true;

            try
            {
                return await operation(cancellationToken);
            }
            finally
            {
                IsActive = false;
            }
        }
    }

    private sealed class FakeGateway : ICsobGatewayClient
    {
        public int ReverseCalls { get; private set; }

        public int StatusCalls { get; private set; }

        public int RefundCalls { get; private set; }

        public Action? OnReverse { get; set; }

        public Action? OnRefund { get; set; }

        public Exception? ReverseException { get; set; }

        public Exception? RefundException { get; set; }

        public CsobPaymentReverseResult ReverseResult { get; set; } =
            Reverse(paymentStatus: 5);

        public CsobPaymentStatusResult StatusResult { get; set; } =
            Status(paymentStatus: 7);

        public CsobPaymentRefundResult RefundResult { get; set; } =
            Refund(paymentStatus: 10);

        public long? LastRefundAmountMinorUnits { get; private set; }

        public Task<CsobPaymentReverseResult> ReverseAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            ReverseCalls++;
            Assert.Equal(PayId, payId);
            OnReverse?.Invoke();

            return ReverseException is null
                ? Task.FromResult(ReverseResult with { PayId = payId })
                : Task.FromException<CsobPaymentReverseResult>(
                    ReverseException);
        }

        public Task<CsobPaymentStatusResult> GetStatusAsync(
            string payId,
            CancellationToken cancellationToken = default)
        {
            StatusCalls++;
            Assert.Equal(PayId, payId);
            return Task.FromResult(StatusResult with { PayId = payId });
        }

        public Task<CsobEchoResult> EchoAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobEchoResult> EchoPostAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentInitResult> InitializeAsync(
            CsobPaymentInit payment,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CsobPaymentRefundResult> RefundAsync(
            string payId,
            long? amountMinorUnits = null,
            CancellationToken cancellationToken = default)
        {
            RefundCalls++;
            LastRefundAmountMinorUnits = amountMinorUnits;
            Assert.Equal(PayId, payId);
            OnRefund?.Invoke();

            return RefundException is null
                ? Task.FromResult(RefundResult with { PayId = payId })
                : Task.FromException<CsobPaymentRefundResult>(
                    RefundException);
        }
    }

    private sealed class RecordingAuditTrail : IAuditTrail
    {
        public List<AuditEntry> Entries { get; } = [];

        public void Stage(AuditEntry entry) => Entries.Add(entry);

        public Task WriteAsync(
            AuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class ExistingJobCoordination : IJobPaymentCoordination
    {
        public Task<bool> LockJobAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> HasBlockingDirectPaymentAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeJobRepository : IJobRepository
    {
        private readonly Job _job;

        public FakeJobRepository(Job job)
        {
            _job = job;
        }

        public Task<Job?> FindByIdAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Job?>(_job.Id == jobId ? _job : null);

        public Task AddAsync(
            Job job,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            Job job,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly Payment _payment;

        public FakePaymentRepository(Payment payment)
        {
            _payment = payment;
        }

        public Task<Payment?> FindByIdAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Payment?>(
                _payment.Id == paymentId ? _payment : null);

        public Task<Payment?> FindBlockingForJobAsync(
            Guid jobId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Payment?> FindByProviderReferenceAsync(
            PaymentProvider provider,
            string providerReference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Payment?> FindByCreationRequestIdAsync(
            Guid creationRequestId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(
            Payment payment,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddPreparedAsync(
            Payment payment,
            PaymentInitiation initiation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            Payment payment,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSettlementReturnRepository :
        ISettlementReturnRepository
    {
        public List<SettlementReturn> Stored { get; } = [];

        public Task<SettlementReturn?> FindByIdAsync(
            Guid settlementReturnId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SettlementReturn?>(
                Stored.SingleOrDefault(item => item.Id == settlementReturnId));

        public Task<SettlementReturn?> FindByRequestIdAsync(
            Guid requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SettlementReturn?>(
                Stored.SingleOrDefault(item => item.RequestId == requestId));

        public Task<SettlementReturn?> FindByOriginalPaymentIdAsync(
            Guid originalPaymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SettlementReturn?>(
                Stored.SingleOrDefault(
                    item => item.OriginalPaymentId == originalPaymentId));

        public Task<SettlementReturn?> FindByJobIdAsync(
            Guid jobId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SettlementReturn?>(
                Stored.SingleOrDefault(item => item.JobId == jobId));

        public Task AddAsync(
            SettlementReturn settlementReturn,
            CancellationToken cancellationToken = default)
        {
            Stored.Add(settlementReturn);
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            SettlementReturn settlementReturn,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeSettlementReturnProviderAttemptRepository :
        ISettlementReturnProviderAttemptRepository
    {
        public List<SettlementReturnProviderAttempt> Stored { get; } = [];

        public Task<SettlementReturnProviderAttempt?> FindByIdAsync(
            Guid attemptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SettlementReturnProviderAttempt?>(
                Stored.SingleOrDefault(item => item.Id == attemptId));

        public Task<SettlementReturnProviderAttempt?>
            FindActiveBySettlementReturnIdAsync(
                Guid settlementReturnId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<SettlementReturnProviderAttempt?>(
                Stored.SingleOrDefault(
                    item =>
                        item.SettlementReturnId == settlementReturnId &&
                        item.IsActive));

        public Task<IReadOnlyList<SettlementReturnProviderAttempt>>
            ListBySettlementReturnIdAsync(
                Guid settlementReturnId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SettlementReturnProviderAttempt>>(
                Stored
                    .Where(item =>
                        item.SettlementReturnId == settlementReturnId)
                    .ToArray());

        public Task AddAsync(
            SettlementReturnProviderAttempt attempt,
            CancellationToken cancellationToken = default)
        {
            Stored.Add(attempt);
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            SettlementReturnProviderAttempt attempt,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private class UnusedQueryProxy : DispatchProxy
    {
        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            throw new NotSupportedException(
                "The successful admin POST must not invoke list queries.");
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
}
