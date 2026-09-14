using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Access.Infrastructure.Entra;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;
using FuaPay.Web.Modules.Credits.Web.PrintPayments;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FuaPay.DatabaseTests;

public sealed class PrintPaymentsApiPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private const string SourceACredential =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SourceBCredential =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static readonly Guid SourceAId = Guid.NewGuid();
    private static readonly Guid SourceBId = Guid.NewGuid();
    private static readonly DateTimeOffset SeedTime =
        new(2026, 8, 27, 8, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public PrintPaymentsApiPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Lifecycle_ValidLinkedCustomerUsesAuthenticatedSourceAndPreservesIdempotency()
    {
        var user = await SeedUserAsync(
            customer: true,
            blocked: false,
            balanceMinorUnits: 1_000);
        var reserveCommandId = Guid.NewGuid();
        var jobUuid = $"urn:uuid:{Guid.NewGuid():D}";

        try
        {
            using var factory = CreateApiFactory();
            using var client = CreateClient(factory, SourceACredential);
            var reserveRequest = ReserveRequest(
                user,
                reserveCommandId,
                jobUuid,
                amountMinorUnits: 400);

            using var reserveResponse = await client.PostAsJsonAsync(
                "/api/print-payments/reservations",
                reserveRequest);
            var reserved = await ReadReservationAsync(reserveResponse);

            using var reserveReplayResponse = await client.PostAsJsonAsync(
                "/api/print-payments/reservations",
                reserveRequest);
            var reserveReplay = await ReadReservationAsync(
                reserveReplayResponse);

            Assert.Equal(reserved, reserveReplay);
            Assert.Equal("Reserved", reserved.Status);
            Assert.Equal(jobUuid, reserved.JobUuid);

            var reservedState = await ReadFinancialStateAsync(user.UserId);

            Assert.Equal(1_000, reservedState.BalanceMinorUnits);
            Assert.Equal(1, reservedState.MovementCount);
            Assert.Equal(1, reservedState.ReservationCount);
            Assert.Equal(400, reservedState.BlockingMinorUnits);
            Assert.Equal(
                1,
                await CountReservationAuditAsync(
                    user.UserId,
                    "print-reservation.reserved"));

            using var lookupResponse = await client.GetAsync(
                "/api/print-payments/reservations?jobUuid=" +
                Uri.EscapeDataString(jobUuid));
            var lookup = await ReadReservationAsync(lookupResponse);

            Assert.Equal(reserved, lookup);
            Assert.Equal(
                SourceAId,
                await ReadReservationSourceAsync(reserved.ReservationId));

            using var idempotencyConflict = await client.PostAsJsonAsync(
                "/api/print-payments/reservations",
                ReserveRequest(
                    user,
                    reserveCommandId,
                    jobUuid,
                    amountMinorUnits: 399));
            await AssertProblemAsync(
                idempotencyConflict,
                HttpStatusCode.Conflict,
                "idempotency_conflict");

            using var jobConflict = await client.PostAsJsonAsync(
                "/api/print-payments/reservations",
                ReserveRequest(
                    user,
                    Guid.NewGuid(),
                    jobUuid,
                    amountMinorUnits: 400));
            await AssertProblemAsync(
                jobConflict,
                HttpStatusCode.Conflict,
                "print_job_conflict");

            var resolutionCommandId = Guid.NewGuid();
            using var resolutionResponse = await client.PostAsJsonAsync(
                $"/api/print-payments/reservations/" +
                $"{reserved.ReservationId:D}/resolution-required",
                new { resolutionCommandId });
            var resolution = await ReadReservationAsync(
                resolutionResponse);
            using var resolutionReplayResponse =
                await client.PostAsJsonAsync(
                    $"/api/print-payments/reservations/" +
                    $"{reserved.ReservationId:D}/resolution-required",
                    new { resolutionCommandId });
            var resolutionReplay = await ReadReservationAsync(
                resolutionReplayResponse);

            Assert.Equal("ResolutionRequired", resolution.Status);
            Assert.Equal(resolution, resolutionReplay);
            Assert.Equal(
                400,
                (await ReadFinancialStateAsync(user.UserId))
                    .BlockingMinorUnits);
            Assert.Equal(
                1,
                await CountReservationAuditAsync(
                    user.UserId,
                    "print-reservation.resolution-required"));

            var captureCommandId = Guid.NewGuid();
            var capturePath =
                $"/api/print-payments/reservations/" +
                $"{reserved.ReservationId:D}/capture";
            using var captureResponse = await client.PostAsJsonAsync(
                capturePath,
                new { terminalCommandId = captureCommandId });
            var captured = await ReadReservationAsync(captureResponse);
            using var captureReplayResponse = await client.PostAsJsonAsync(
                capturePath,
                new { terminalCommandId = captureCommandId });
            var captureReplay = await ReadReservationAsync(
                captureReplayResponse);

            Assert.Equal(captured, captureReplay);
            Assert.Equal("Captured", captured.Status);
            Assert.NotNull(captured.DebitOperationId);

            var capturedState = await ReadFinancialStateAsync(user.UserId);

            Assert.Equal(600, capturedState.BalanceMinorUnits);
            Assert.Equal(2, capturedState.MovementCount);
            Assert.Equal(0, capturedState.BlockingMinorUnits);
            Assert.Equal(
                1,
                await CountReservationAuditAsync(
                    user.UserId,
                    "print-reservation.captured"));

            using var invalidReleaseResponse =
                await client.PostAsJsonAsync(
                    $"/api/print-payments/reservations/" +
                    $"{captured.ReservationId:D}/release",
                    new { terminalCommandId = Guid.NewGuid() });
            await AssertProblemAsync(
                invalidReleaseResponse,
                HttpStatusCode.Conflict,
                "reservation_conflict");

            var releaseReservationResponse = await client.PostAsJsonAsync(
                "/api/print-payments/reservations",
                ReserveRequest(
                    user,
                    Guid.NewGuid(),
                    $"urn:uuid:{Guid.NewGuid():D}",
                    amountMinorUnits: 200));
            var releaseReservation = await ReadReservationAsync(
                releaseReservationResponse);
            var releaseCommandId = Guid.NewGuid();
            var releasePath =
                $"/api/print-payments/reservations/" +
                $"{releaseReservation.ReservationId:D}/release";
            using var releaseResponse = await client.PostAsJsonAsync(
                releasePath,
                new { terminalCommandId = releaseCommandId });
            var released = await ReadReservationAsync(releaseResponse);
            using var releaseReplayResponse = await client.PostAsJsonAsync(
                releasePath,
                new { terminalCommandId = releaseCommandId });
            var releaseReplay = await ReadReservationAsync(
                releaseReplayResponse);

            Assert.Equal(released, releaseReplay);
            Assert.Equal("Released", released.Status);

            var finalState = await ReadFinancialStateAsync(user.UserId);

            Assert.Equal(600, finalState.BalanceMinorUnits);
            Assert.Equal(2, finalState.MovementCount);
            Assert.Equal(2, finalState.ReservationCount);
            Assert.Equal(0, finalState.BlockingMinorUnits);
            Assert.Equal(
                2,
                await CountReservationAuditAsync(
                    user.UserId,
                    "print-reservation.reserved"));
            Assert.Equal(
                1,
                await CountReservationAuditAsync(
                    user.UserId,
                    "print-reservation.released"));
        }
        finally
        {
            await DeleteScenarioAsync(user.UserId);
        }
    }

    [Fact]
    public async Task Reserve_UnknownIdentityWithMatchingEmailDoesNotProvisionAnything()
    {
        var linkedUser = await SeedUserAsync(
            customer: true,
            blocked: false,
            balanceMinorUnits: null,
            email: "same-profile@example.cz");
        var unknownIdentity = linkedUser with
        {
            ObjectId = Guid.NewGuid()
        };

        try
        {
            var countsBefore = await ReadGlobalCountsAsync();
            using var factory = CreateApiFactory();
            using var client = CreateClient(factory, SourceACredential);

            using var response = await client.PostAsJsonAsync(
                "/api/print-payments/reservations",
                ReserveRequest(
                    unknownIdentity,
                    Guid.NewGuid(),
                    $"urn:uuid:{Guid.NewGuid():D}",
                    amountMinorUnits: 100));

            await AssertProblemAsync(
                response,
                HttpStatusCode.NotFound,
                "identity_not_linked");
            Assert.Equal(countsBefore, await ReadGlobalCountsAsync());
        }
        finally
        {
            await DeleteScenarioAsync(linkedUser.UserId);
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Reserve_BlockedOrNonCustomerUserIsDeniedBeforeFinancialMutation(
        bool customer,
        bool blocked)
    {
        var user = await SeedUserAsync(
            customer,
            blocked,
            balanceMinorUnits: 1_000);

        try
        {
            using var factory = CreateApiFactory();
            using var client = CreateClient(factory, SourceACredential);

            using var response = await client.PostAsJsonAsync(
                "/api/print-payments/reservations",
                ReserveRequest(
                    user,
                    Guid.NewGuid(),
                    $"urn:uuid:{Guid.NewGuid():D}",
                    amountMinorUnits: 100));

            await AssertProblemAsync(
                response,
                HttpStatusCode.Forbidden,
                "user_not_eligible");

            var state = await ReadFinancialStateAsync(user.UserId);

            Assert.Equal(1_000, state.BalanceMinorUnits);
            Assert.Equal(1, state.MovementCount);
            Assert.Equal(0, state.ReservationCount);
            Assert.Equal(0, state.BlockingMinorUnits);
        }
        finally
        {
            await DeleteScenarioAsync(user.UserId);
        }
    }

    [Fact]
    public async Task Reserve_InsufficientAvailableCreditCreatesNoReservation()
    {
        var user = await SeedUserAsync(
            customer: true,
            blocked: false,
            balanceMinorUnits: 300);

        try
        {
            using var factory = CreateApiFactory();
            using var client = CreateClient(factory, SourceACredential);

            using var response = await client.PostAsJsonAsync(
                "/api/print-payments/reservations",
                ReserveRequest(
                    user,
                    Guid.NewGuid(),
                    $"urn:uuid:{Guid.NewGuid():D}",
                    amountMinorUnits: 400));

            await AssertProblemAsync(
                response,
                HttpStatusCode.Conflict,
                "insufficient_credit");

            var state = await ReadFinancialStateAsync(user.UserId);

            Assert.Equal(300, state.BalanceMinorUnits);
            Assert.Equal(1, state.MovementCount);
            Assert.Equal(0, state.ReservationCount);
            Assert.Equal(0, state.BlockingMinorUnits);
            Assert.Equal(
                0,
                await CountReservationAuditAsync(
                    user.UserId,
                    "print-reservation.reserved"));
        }
        finally
        {
            await DeleteScenarioAsync(user.UserId);
        }
    }

    [Fact]
    public async Task SourceB_CannotLookupOrMutateSourceAReservation()
    {
        var user = await SeedUserAsync(
            customer: true,
            blocked: false,
            balanceMinorUnits: 1_000);
        var jobUuid = $"urn:uuid:{Guid.NewGuid():D}";

        try
        {
            using var factory = CreateApiFactory();
            using var sourceAClient = CreateClient(
                factory,
                SourceACredential);
            using var sourceBClient = CreateClient(
                factory,
                SourceBCredential);
            using var reserveResponse =
                await sourceAClient.PostAsJsonAsync(
                    "/api/print-payments/reservations",
                    ReserveRequest(
                        user,
                        Guid.NewGuid(),
                        jobUuid,
                        amountMinorUnits: 400));
            var reserved = await ReadReservationAsync(reserveResponse);

            using var lookupResponse = await sourceBClient.GetAsync(
                "/api/print-payments/reservations?jobUuid=" +
                Uri.EscapeDataString(jobUuid));
            await AssertProblemAsync(
                lookupResponse,
                HttpStatusCode.NotFound,
                "reservation_not_found");

            using var resolutionResponse =
                await sourceBClient.PostAsJsonAsync(
                    $"/api/print-payments/reservations/" +
                    $"{reserved.ReservationId:D}/resolution-required",
                    new { resolutionCommandId = Guid.NewGuid() });
            await AssertProblemAsync(
                resolutionResponse,
                HttpStatusCode.NotFound,
                "reservation_not_found");

            using var captureResponse = await sourceBClient.PostAsJsonAsync(
                $"/api/print-payments/reservations/" +
                $"{reserved.ReservationId:D}/capture",
                new { terminalCommandId = Guid.NewGuid() });
            await AssertProblemAsync(
                captureResponse,
                HttpStatusCode.NotFound,
                "reservation_not_found");

            using var releaseResponse = await sourceBClient.PostAsJsonAsync(
                $"/api/print-payments/reservations/" +
                $"{reserved.ReservationId:D}/release",
                new { terminalCommandId = Guid.NewGuid() });
            await AssertProblemAsync(
                releaseResponse,
                HttpStatusCode.NotFound,
                "reservation_not_found");

            var state = await ReadFinancialStateAsync(user.UserId);

            Assert.Equal(1_000, state.BalanceMinorUnits);
            Assert.Equal(1, state.MovementCount);
            Assert.Equal(1, state.ReservationCount);
            Assert.Equal(400, state.BlockingMinorUnits);
            Assert.Equal(
                1,
                await CountReservationAuditAsync(
                    user.UserId,
                    "print-reservation.reserved"));
            Assert.Equal(
                0,
                await CountReservationAuditAsync(
                    user.UserId,
                    "print-reservation.captured"));
        }
        finally
        {
            await DeleteScenarioAsync(user.UserId);
        }
    }

    [Fact]
    public async Task ByCredential_RecoveryLookupSurvivesPinChangeAndUsesCanonicalLifecycle()
    {
        const string email = "persistent-print@example.cz";
        const string firstCode = "123456";
        const string changedCode = "654321";
        var user = await SeedUserAsync(
            customer: true,
            blocked: false,
            balanceMinorUnits: 1_000,
            email: email);

        try
        {
            using var factory = CreateApiFactory();
            await SetPrintCodeAsync(factory, user.UserId, firstCode);
            using var client = CreateClient(factory, SourceACredential);
            var commandId = Guid.NewGuid();
            var jobUuid = $"urn:uuid:{Guid.NewGuid():D}";
            var request = CredentialReserveRequest(
                " PERSISTENT-PRINT@EXAMPLE.CZ ",
                firstCode,
                commandId,
                jobUuid,
                400);

            using var response = await client.PostAsJsonAsync(
                "/api/print-payments/reservations/by-credential",
                request);
            var reserved = await ReadReservationAsync(response);
            var state = await ReadFinancialStateAsync(user.UserId);
            Assert.Equal(1_000, state.BalanceMinorUnits);
            Assert.Equal(1, state.MovementCount);
            Assert.Equal(1, state.ReservationCount);
            Assert.Equal(400, state.BlockingMinorUnits);
            await AssertPrintCodeAbsentFromPersistenceAsync(user.UserId, firstCode);

            using var insufficientResponse = await client.PostAsJsonAsync(
                "/api/print-payments/reservations/by-credential",
                CredentialReserveRequest(
                    email,
                    firstCode,
                    Guid.NewGuid(),
                    $"urn:uuid:{Guid.NewGuid():D}",
                    700));
            await AssertProblemAsync(
                insufficientResponse,
                HttpStatusCode.Conflict,
                "insufficient_credit");
            Assert.Equal(
                1,
                (await ReadFinancialStateAsync(user.UserId)).ReservationCount);

            using var captureResponse = await client.PostAsJsonAsync(
                $"/api/print-payments/reservations/{reserved.ReservationId:D}/capture",
                new { terminalCommandId = Guid.NewGuid() });
            var captured = await ReadReservationAsync(captureResponse);
            Assert.Equal("Captured", captured.Status);
            var capturedState = await ReadFinancialStateAsync(user.UserId);
            Assert.Equal(600, capturedState.BalanceMinorUnits);
            Assert.Equal(2, capturedState.MovementCount);
            Assert.Equal(1, capturedState.ReservationCount);
            Assert.Equal(0, capturedState.BlockingMinorUnits);

            await SetPrintCodeAsync(factory, user.UserId, changedCode);
            using var recoveryResponse = await client.GetAsync(
                "/api/print-payments/reservations?jobUuid=" +
                Uri.EscapeDataString(jobUuid));
            var recovered = await ReadReservationAsync(recoveryResponse);
            Assert.Equal(captured, recovered);

            using var oldCodeReplayResponse = await client.PostAsJsonAsync(
                "/api/print-payments/reservations/by-credential",
                request);
            await AssertProblemAsync(
                oldCodeReplayResponse,
                HttpStatusCode.Unauthorized,
                "print_credential_authentication_failed");

            using var changedCodeResponse = await client.PostAsJsonAsync(
                "/api/print-payments/reservations/by-credential",
                CredentialReserveRequest(
                    email,
                    changedCode,
                    Guid.NewGuid(),
                    $"urn:uuid:{Guid.NewGuid():D}",
                    100));
            _ = await ReadReservationAsync(changedCodeResponse);

            await RevokePrintCodeAsync(factory, user.UserId);
            using var revokedResponse = await client.PostAsJsonAsync(
                "/api/print-payments/reservations/by-credential",
                CredentialReserveRequest(
                    email,
                    changedCode,
                    Guid.NewGuid(),
                    $"urn:uuid:{Guid.NewGuid():D}",
                    100));
            await AssertProblemAsync(
                revokedResponse,
                HttpStatusCode.Unauthorized,
                "print_credential_authentication_failed");
        }
        finally
        {
            await DeleteScenarioAsync(user.UserId);
        }
    }

    [Fact]
    public async Task ByCredential_UnknownEmailAndWrongCodeAreIndistinguishable()
    {
        const string email = "generic-failure@example.cz";
        var user = await SeedUserAsync(
            customer: true,
            blocked: false,
            balanceMinorUnits: 1_000,
            email: email);

        try
        {
            var countingHasher = new CountingPrintCodeHasher();
            using var factory = CreateCountingHasherApiFactory(
                countingHasher);
            await SetPrintCodeAsync(factory, user.UserId, "123456");
            using var client = CreateClient(factory, SourceACredential);

            using var wrongCode = await client.PostAsJsonAsync(
                "/api/print-payments/reservations/by-credential",
                CredentialReserveRequest(
                    email,
                    "000000",
                    Guid.NewGuid(),
                    $"urn:uuid:{Guid.NewGuid():D}",
                    100));
            using var unknownEmail = await client.PostAsJsonAsync(
                "/api/print-payments/reservations/by-credential",
                CredentialReserveRequest(
                    "unknown@example.cz",
                    "000000",
                    Guid.NewGuid(),
                    $"urn:uuid:{Guid.NewGuid():D}",
                    100));

            Assert.Equal(wrongCode.StatusCode, unknownEmail.StatusCode);
            Assert.Equal(
                await wrongCode.Content.ReadAsStringAsync(),
                await unknownEmail.Content.ReadAsStringAsync());
            Assert.Equal(2, countingHasher.HashCalls);
            Assert.Equal(2, countingHasher.VerifyCalls);
            Assert.Equal(0, (await ReadFinancialStateAsync(user.UserId)).ReservationCount);
        }
        finally
        {
            await DeleteScenarioAsync(user.UserId);
        }
    }

    [Fact]
    public async Task ByCredential_ReservationAndConcurrentRevocationAreSerialized()
    {
        const string email = "credential-race@example.cz";
        const string printCode = "123456";
        var user = await SeedUserAsync(
            customer: true,
            blocked: false,
            balanceMinorUnits: 1_000,
            email: email);
        var gate = new CredentialRaceGate();

        try
        {
            using var factory = CreateCredentialRaceApiFactory(gate);
            await SetPrintCodeAsync(factory, user.UserId, printCode);
            using var client = CreateClient(factory, SourceACredential);
            var requestTask = client.PostAsJsonAsync(
                "/api/print-payments/reservations/by-credential",
                CredentialReserveRequest(
                    email,
                    printCode,
                    Guid.NewGuid(),
                    $"urn:uuid:{Guid.NewGuid():D}",
                    100));

            await gate.AuthenticationLocked.WaitAsync(TimeSpan.FromSeconds(30));
            var revokeTask = RevokePrintCodeAsync(factory, user.UserId);
            await gate.RevocationBlocked.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.False(revokeTask.IsCompleted);
            gate.ReleaseAuthentication();

            using var response = await requestTask;
            _ = await ReadReservationAsync(response);
            await revokeTask;

            using var revokedResponse = await client.PostAsJsonAsync(
                "/api/print-payments/reservations/by-credential",
                CredentialReserveRequest(
                    email,
                    printCode,
                    Guid.NewGuid(),
                    $"urn:uuid:{Guid.NewGuid():D}",
                    100));
            await AssertProblemAsync(
                revokedResponse,
                HttpStatusCode.Unauthorized,
                "print_credential_authentication_failed");
            Assert.Equal(
                1,
                (await ReadFinancialStateAsync(user.UserId)).ReservationCount);
        }
        finally
        {
            gate.ReleaseAuthentication();
            await DeleteScenarioAsync(user.UserId);
        }
    }

    [Fact]
    public async Task ByCredential_SuccessfulUsesDoNotConsumeFailedGuessBudget()
    {
        const string email = "successful-printing@example.cz";
        const string printCode = "123456";
        var user = await SeedUserAsync(
            customer: true,
            blocked: false,
            balanceMinorUnits: 1_000,
            email: email);

        try
        {
            using var factory = CreateApiFactory();
            await SetPrintCodeAsync(factory, user.UserId, printCode);
            using var client = CreateClient(factory, SourceACredential);

            for (var attempt = 0; attempt < 7; attempt++)
            {
                using var response = await client.PostAsJsonAsync(
                    "/api/print-payments/reservations/by-credential",
                    CredentialReserveRequest(
                        email,
                        printCode,
                        Guid.NewGuid(),
                        $"urn:uuid:{Guid.NewGuid():D}",
                        1));
                _ = await ReadReservationAsync(response);
            }

            var state = await ReadFinancialStateAsync(user.UserId);
            Assert.Equal(7, state.ReservationCount);
            Assert.Equal(7, state.BlockingMinorUnits);
        }
        finally
        {
            await DeleteScenarioAsync(user.UserId);
        }
    }

    [Fact]
    public async Task PrintCredentialManagement_ConcurrentSetAndRevokeDoNotLeakPersistenceFailures()
    {
        const string email = "management-race@example.cz";
        var user = await SeedUserAsync(
            customer: true,
            blocked: false,
            balanceMinorUnits: null,
            email: email);

        try
        {
            using var seedFactory = CreateApiFactory();
            await SetPrintCodeAsync(seedFactory, user.UserId, "111111");

            using var setFactory = CreateManagementRaceApiFactory(
                new TwoCallBarrier());
            await Task.WhenAll(
                SetPrintCodeAsync(setFactory, user.UserId, "222222"),
                SetPrintCodeAsync(setFactory, user.UserId, "333333"));

            using var setRevokeFactory = CreateManagementRaceApiFactory(
                new TwoCallBarrier());
            await Task.WhenAll(
                SetPrintCodeAsync(setRevokeFactory, user.UserId, "444444"),
                RevokePrintCodeAsync(setRevokeFactory, user.UserId));

            using var scope = seedFactory.Services.CreateScope();
            var candidate = await scope.ServiceProvider
                .GetRequiredService<IPrintCredentialRepository>()
                .FindAuthenticationCandidateAsync(email);

            if (candidate is not null)
            {
                var hasher = scope.ServiceProvider
                    .GetRequiredService<IPrintCodeHasher>();
                Assert.True(hasher.Verify(candidate.CodeHash, "444444"));
                Assert.False(hasher.Verify(candidate.CodeHash, "111111"));
            }
        }
        finally
        {
            await DeleteScenarioAsync(user.UserId);
        }
    }

    [Fact]
    public async Task PrintCredentialManagement_ConcurrentInitialSetRetriesWithCleanTrackedState()
    {
        const string email = "initial-set-race@example.cz";
        var user = await SeedUserAsync(
            customer: true,
            blocked: false,
            balanceMinorUnits: null,
            email: email);

        try
        {
            using var factory = CreateManagementRaceApiFactory(
                new TwoCallBarrier());

            await Task.WhenAll(
                SetPrintCodeAsync(factory, user.UserId, "111111"),
                SetPrintCodeAsync(factory, user.UserId, "222222"));

            using var scope = factory.Services.CreateScope();
            var candidate = await scope.ServiceProvider
                .GetRequiredService<IPrintCredentialRepository>()
                .FindAuthenticationCandidateAsync(email);
            var hasher = scope.ServiceProvider
                .GetRequiredService<IPrintCodeHasher>();

            Assert.NotNull(candidate);
            Assert.True(
                hasher.Verify(candidate.CodeHash, "111111") ||
                hasher.Verify(candidate.CodeHash, "222222"));
        }
        finally
        {
            await DeleteScenarioAsync(user.UserId);
        }
    }

    [Fact]
    public async Task PrintCredentialManagement_StaleEmailUniqueConflictFailsClosed()
    {
        const string reassignedEmail = "reassigned-print@example.cz";
        var originalOwner = await SeedUserAsync(
            customer: true,
            blocked: false,
            balanceMinorUnits: null,
            email: reassignedEmail);
        SeededUser? newOwner = null;

        try
        {
            using var factory = CreateApiFactory();
            await SetPrintCodeAsync(factory, originalOwner.UserId, "111111");
            await ChangeAccessEmailAsync(
                originalOwner.UserId,
                "former-owner@example.cz");
            newOwner = await SeedUserAsync(
                customer: true,
                blocked: false,
                balanceMinorUnits: null,
                email: reassignedEmail);

            await Assert.ThrowsAsync<PrintCredentialUnavailableException>(
                () => SetPrintCodeAsync(
                    factory,
                    newOwner.UserId,
                    "222222"));
        }
        finally
        {
            if (newOwner is not null)
            {
                await DeleteScenarioAsync(newOwner.UserId);
            }

            await DeleteScenarioAsync(originalOwner.UserId);
        }
    }

    [Fact]
    public async Task ByCredential_UnicodeCompatibilityNormalizationMatchesPostgresLookup()
    {
        var user = await SeedUserAsync(
            customer: true,
            blocked: false,
            balanceMinorUnits: 100,
            email: "ｓtudent@tul.cz");

        try
        {
            using var factory = CreateApiFactory();
            await SetPrintCodeAsync(factory, user.UserId, "123456");
            using var client = CreateClient(factory, SourceACredential);

            using var response = await client.PostAsJsonAsync(
                "/api/print-payments/reservations/by-credential",
                CredentialReserveRequest(
                    "student@tul.cz",
                    "123456",
                    Guid.NewGuid(),
                    $"urn:uuid:{Guid.NewGuid():D}",
                    1));

            _ = await ReadReservationAsync(response);
        }
        finally
        {
            await DeleteScenarioAsync(user.UserId);
        }
    }

    [Fact]
    public async Task PrintCredentialManagement_UnicodeEquivalentAmbiguityFailsClosed()
    {
        var first = await SeedUserAsync(
            customer: true,
            blocked: false,
            balanceMinorUnits: null,
            email: "ｓtudent-ambiguous@tul.cz");
        var second = await SeedUserAsync(
            customer: true,
            blocked: false,
            balanceMinorUnits: null,
            email: "student-ambiguous@tul.cz");

        try
        {
            using var factory = CreateApiFactory();

            await Assert.ThrowsAsync<PrintCredentialUnavailableException>(
                () => SetPrintCodeAsync(factory, first.UserId, "123456"));
        }
        finally
        {
            await DeleteScenarioAsync(second.UserId);
            await DeleteScenarioAsync(first.UserId);
        }
    }

    private WebApplicationFactory<Program> CreateApiFactory()
    {
        return new ApiWebApplicationFactory();
    }

    private static WebApplicationFactory<Program> CreateCredentialRaceApiFactory(
        CredentialRaceGate gate)
    {
        return new ApiWebApplicationFactory().WithWebHostBuilder(
            builder => builder.ConfigureTestServices(
                services =>
                {
                    var descriptor = Assert.Single(
                        services,
                        item => item.ServiceType ==
                            typeof(IPrintCredentialRepository));

                    services.Remove(descriptor);
                    services.AddScoped<IPrintCredentialRepository>(
                        provider => new CoordinatingPrintCredentialRepository(
                            CreateOriginalPrintCredentialRepository(
                                provider,
                                descriptor),
                            gate));
                }));
    }

    private static WebApplicationFactory<Program> CreateManagementRaceApiFactory(
        TwoCallBarrier barrier)
    {
        return new ApiWebApplicationFactory().WithWebHostBuilder(
            builder => builder.ConfigureTestServices(
                services =>
                {
                    var descriptor = Assert.Single(
                        services,
                        item => item.ServiceType ==
                            typeof(IPrintCredentialRepository));

                    services.Remove(descriptor);
                    services.AddScoped<IPrintCredentialRepository>(
                        provider => new ManagementBarrierPrintCredentialRepository(
                            CreateOriginalPrintCredentialRepository(
                                provider,
                                descriptor),
                            barrier));
                }));
    }

    private static WebApplicationFactory<Program> CreateCountingHasherApiFactory(
        CountingPrintCodeHasher hasher)
    {
        return new ApiWebApplicationFactory().WithWebHostBuilder(
            builder => builder.ConfigureTestServices(
                services =>
                {
                    var descriptor = Assert.Single(
                        services,
                        item => item.ServiceType == typeof(IPrintCodeHasher));
                    services.Remove(descriptor);
                    services.AddSingleton<IPrintCodeHasher>(
                        provider =>
                        {
                            hasher.Initialize(
                                CreateOriginalPrintCodeHasher(
                                    provider,
                                    descriptor));
                            return hasher;
                        });
                }));
    }

    private static IPrintCodeHasher CreateOriginalPrintCodeHasher(
        IServiceProvider provider,
        ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is IPrintCodeHasher instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (IPrintCodeHasher)
                descriptor.ImplementationFactory(provider);
        }

        return (IPrintCodeHasher)
            ActivatorUtilities.CreateInstance(
                provider,
                descriptor.ImplementationType
                    ?? throw new InvalidOperationException(
                        "The original print-code hasher has no implementation."));
    }

    private static IPrintCredentialRepository CreateOriginalPrintCredentialRepository(
        IServiceProvider provider,
        ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is
            IPrintCredentialRepository instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (IPrintCredentialRepository)
                descriptor.ImplementationFactory(provider);
        }

        return (IPrintCredentialRepository)
            ActivatorUtilities.CreateInstance(
                provider,
                descriptor.ImplementationType
                    ?? throw new InvalidOperationException(
                        "The original print-credential repository has no implementation."));
    }

    private static IReadOnlyDictionary<string, string?> ApiSettings()
    {
        return new Dictionary<string, string?>
        {
            ["PrintPayments:Enabled"] = "true",
            ["PrintCredentials:Enabled"] = "true",
            ["PrintCredentials:PepperBase64"] =
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
            ["PrintPayments:Sources:0:PrintSourceId"] =
                SourceAId.ToString("D"),
            ["PrintPayments:Sources:0:CredentialSha256"] =
                Digest(SourceACredential),
            ["PrintPayments:Sources:1:PrintSourceId"] =
                SourceBId.ToString("D"),
            ["PrintPayments:Sources:1:CredentialSha256"] =
                Digest(SourceBCredential)
        };
    }

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory,
        string credential)
    {
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", credential);
        return client;
    }

    private async Task<SeededUser> SeedUserAsync(
        bool customer,
        bool blocked,
        long? balanceMinorUnits,
        string? email = "student@example.cz")
    {
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var identityKey = ExternalIdentityKey.FromGuidIdentifiers(
            EntraAuthenticationDefaults.ExternalIdentityProvider,
            tenantId.ToString("D"),
            objectId.ToString("D"));
        var user = new AccessUser(
            Guid.NewGuid(),
            "Print API student",
            email,
            SeedTime);

        if (customer)
        {
            user.GrantRole(
                Guid.NewGuid(),
                AccessRole.Customer,
                SeedTime,
                RoleChangeActor.ForProcess("database-test"));
        }

        if (blocked)
        {
            user.Block();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<IAccessUserRepository>()
                .AddAsync(
                    user,
                    identityKey,
                    CancellationToken.None);
        }

        if (balanceMinorUnits.HasValue)
        {
            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<CreditService>()
                .CreditAsync(
                    user.Id,
                    Guid.NewGuid(),
                    new Money(balanceMinorUnits.Value),
                    "Print API test balance");
        }

        return new SeededUser(
            user.Id,
            tenantId,
            objectId);
    }

    private static object ReserveRequest(
        SeededUser user,
        Guid reserveCommandId,
        string jobUuid,
        long amountMinorUnits)
    {
        return new
        {
            reserveCommandId,
            jobUuid,
            userIdentity = new
            {
                provider = EntraAuthenticationDefaults
                    .ExternalIdentityProvider,
                tenantId = user.TenantId.ToString("D"),
                objectId = user.ObjectId.ToString("D")
            },
            amountMinorUnits,
            currency = "CZK"
        };
    }

    private static object CredentialReserveRequest(
        string email,
        string printCode,
        Guid reserveCommandId,
        string jobUuid,
        long amountMinorUnits) =>
        new
        {
            email,
            printCode,
            reserveCommandId,
            jobUuid,
            amountMinorUnits,
            currency = "CZK"
        };

    private static async Task SetPrintCodeAsync(
        WebApplicationFactory<Program> factory,
        Guid ownerId,
        string printCode)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<PrintCredentialService>()
            .SetAsync(ownerId, printCode, printCode);
    }

    private static async Task RevokePrintCodeAsync(
        WebApplicationFactory<Program> factory,
        Guid ownerId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider
            .GetRequiredService<PrintCredentialService>()
            .RevokeAsync(ownerId);
    }

    private async Task ChangeAccessEmailAsync(
        Guid ownerId,
        string email)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE access.users SET email = {email} WHERE id = {ownerId}");
    }

    private async Task AssertPrintCodeAbsentFromPersistenceAsync(
        Guid ownerId,
        string printCode)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FuaPayDbContext>();
        var leaked = await dbContext.Database.SqlQuery<int>(
            $"""
            SELECT (
                EXISTS (
                    SELECT 1 FROM credits.print_credentials
                    WHERE owner_id = {ownerId}
                      AND code_hash LIKE {'%' + printCode + '%'})
                OR EXISTS (
                    SELECT 1 FROM audit.events
                    WHERE actor_user_id = {ownerId}
                      AND description LIKE {'%' + printCode + '%'})
            )::integer AS "Value"
            """).SingleAsync();
        Assert.Equal(0, leaked);
    }

    private static async Task<PrintPaymentReservationResponse>
        ReadReservationAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content
            .ReadFromJsonAsync<PrintPaymentReservationResponse>();
        return Assert.IsType<PrintPaymentReservationResponse>(result);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.Equal(
            expectedCode,
            document.RootElement
                .GetProperty("code")
                .GetString());
    }

    private async Task<FinancialState> ReadFinancialStateAsync(
        Guid ownerId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        return await dbContext.Database.SqlQuery<FinancialState>(
            $"""
            SELECT
                account.balance_minor_units AS "BalanceMinorUnits",
                (
                    SELECT count(*)::integer
                    FROM credits.movements AS movement
                    WHERE movement.account_id = account.id
                ) AS "MovementCount",
                (
                    SELECT count(*)::integer
                    FROM credits.print_reservations AS reservation
                    WHERE reservation.credit_account_id = account.id
                ) AS "ReservationCount",
                (
                    SELECT COALESCE(sum(reservation.amount_minor_units), 0)::bigint
                    FROM credits.print_reservations AS reservation
                    WHERE reservation.credit_account_id = account.id
                      AND reservation.status IN (1, 2)
                ) AS "BlockingMinorUnits"
            FROM credits.accounts AS account
            WHERE account.owner_id = {ownerId}
            """)
            .SingleAsync();
    }

    private async Task<GlobalCounts> ReadGlobalCountsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        return await dbContext.Database.SqlQuery<GlobalCounts>(
            $"""
            SELECT
                (SELECT count(*)::integer FROM access.users) AS "UserCount",
                (SELECT count(*)::integer FROM credits.accounts) AS "AccountCount",
                (SELECT count(*)::integer FROM credits.print_reservations) AS "ReservationCount"
            """)
            .SingleAsync();
    }

    private async Task<Guid> ReadReservationSourceAsync(
        Guid reservationId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        return await dbContext.Database.SqlQuery<Guid>(
            $"""
            SELECT print_source_id AS "Value"
            FROM credits.print_reservations
            WHERE id = {reservationId}
            """)
            .SingleAsync();
    }

    private async Task<int> CountReservationAuditAsync(
        Guid ownerId,
        string action)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        return await dbContext.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::integer AS "Value"
            FROM audit.events AS audit
            WHERE audit.entity_type = 'print-reservation'
              AND audit.action = {action}
              AND audit.entity_id IN
              (
                  SELECT reservation.id::text
                  FROM credits.print_reservations AS reservation
                  JOIN credits.accounts AS account
                    ON account.id = reservation.credit_account_id
                  WHERE account.owner_id = {ownerId}
              )
            """)
            .SingleAsync();
    }

    private async Task DeleteScenarioAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync();

        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM audit.events
            WHERE actor_user_id = {userId}
               OR (entity_type = 'access-user' AND entity_id = {userId.ToString()})
               OR (
                   entity_type = 'print-reservation'
                   AND entity_id IN
                   (
                       SELECT reservation.id::text
                       FROM credits.print_reservations AS reservation
                       JOIN credits.accounts AS account
                         ON account.id = reservation.credit_account_id
                       WHERE account.owner_id = {userId}
                   )
               )
            """);
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.print_reservations
            WHERE credit_account_id IN
            (
                SELECT id FROM credits.accounts WHERE owner_id = {userId}
            )
            """);
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM credits.movements
            WHERE account_id IN
            (
                SELECT id FROM credits.accounts WHERE owner_id = {userId}
            )
            """);
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM credits.print_credentials WHERE owner_id = {userId}");
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM credits.accounts WHERE owner_id = {userId}");
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM access.role_assignments
            WHERE user_id = {userId}
               OR granted_by_user_id = {userId}
               OR revoked_by_user_id = {userId}
            """);
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM access.external_identities WHERE user_id = {userId}");
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM access.users WHERE id = {userId}");

        await transaction.CommitAsync();
    }

    private static string Digest(string credential) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.ASCII.GetBytes(credential)))
            .ToLowerInvariant();

    private sealed record SeededUser(
        Guid UserId,
        Guid TenantId,
        Guid ObjectId);

    private sealed record FinancialState(
        long BalanceMinorUnits,
        int MovementCount,
        int ReservationCount,
        long BlockingMinorUnits);

    private sealed record GlobalCounts(
        int UserCount,
        int AccountCount,
        int ReservationCount);

    private sealed class TwoCallBarrier
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public async Task SignalAndWaitAsync(
            CancellationToken cancellationToken)
        {
            var arrival = Interlocked.Increment(ref _arrivals);
            if (arrival == 2)
            {
                _release.TrySetResult();
            }

            if (arrival <= 2)
            {
                await _release.Task.WaitAsync(
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
            }
        }
    }

    private sealed class ManagementBarrierPrintCredentialRepository :
        IPrintCredentialRepository
    {
        private readonly IPrintCredentialRepository _inner;
        private readonly TwoCallBarrier _barrier;

        public ManagementBarrierPrintCredentialRepository(
            IPrintCredentialRepository inner,
            TwoCallBarrier barrier)
        {
            _inner = inner;
            _barrier = barrier;
        }

        public async Task<PrintCredential?> FindByOwnerAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default)
        {
            var credential = await _inner.FindByOwnerAsync(
                ownerId,
                cancellationToken);
            await _barrier.SignalAndWaitAsync(cancellationToken);
            return credential;
        }

        public Task<PrintCredentialAuthenticationCandidate?>
            FindAuthenticationCandidateAsync(
                string normalizedEmail,
                CancellationToken cancellationToken = default) =>
            _inner.FindAuthenticationCandidateAsync(
                normalizedEmail,
                cancellationToken);

        public Task<long> CountAccessUsersByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default) =>
            _inner.CountAccessUsersByNormalizedEmailAsync(
                normalizedEmail,
                cancellationToken);

        public void Add(PrintCredential credential) =>
            _inner.Add(credential);

        public Task SaveAsync(
            CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(cancellationToken);
    }

    private sealed class CredentialRaceGate
    {
        private readonly TaskCompletionSource _authenticationLocked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseAuthentication =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _revocationBlocked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task AuthenticationLocked => _authenticationLocked.Task;

        public Task RevocationBlocked => _revocationBlocked.Task;

        public bool IsHoldingAuthentication =>
            _authenticationLocked.Task.IsCompleted &&
            !_releaseAuthentication.Task.IsCompleted;

        public async Task HoldAuthenticationAsync(
            CancellationToken cancellationToken)
        {
            _authenticationLocked.TrySetResult();
            await _releaseAuthentication.Task.WaitAsync(cancellationToken);
        }

        public void RevocationIsBlocked() =>
            _revocationBlocked.TrySetResult();

        public void ReleaseAuthentication() =>
            _releaseAuthentication.TrySetResult();
    }

    private sealed class CoordinatingPrintCredentialRepository :
        IPrintCredentialRepository
    {
        private static readonly TimeSpan BlockingObservation =
            TimeSpan.FromMilliseconds(250);

        private readonly IPrintCredentialRepository _inner;
        private readonly CredentialRaceGate _gate;

        public CoordinatingPrintCredentialRepository(
            IPrintCredentialRepository inner,
            CredentialRaceGate gate)
        {
            _inner = inner;
            _gate = gate;
        }

        public Task<PrintCredential?> FindByOwnerAsync(
            Guid ownerId,
            CancellationToken cancellationToken = default) =>
            _inner.FindByOwnerAsync(ownerId, cancellationToken);

        public async Task<PrintCredentialAuthenticationCandidate?>
            FindAuthenticationCandidateAsync(
                string normalizedEmail,
                CancellationToken cancellationToken = default)
        {
            var candidate = await _inner.FindAuthenticationCandidateAsync(
                normalizedEmail,
                cancellationToken);
            await _gate.HoldAuthenticationAsync(cancellationToken);
            return candidate;
        }

        public Task<long> CountAccessUsersByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default) =>
            _inner.CountAccessUsersByNormalizedEmailAsync(
                normalizedEmail,
                cancellationToken);

        public void Add(PrintCredential credential) =>
            _inner.Add(credential);

        public async Task SaveAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_gate.IsHoldingAuthentication)
            {
                await _inner.SaveAsync(cancellationToken);
                return;
            }

            var saveTask = _inner.SaveAsync(cancellationToken);
            var completed = await Task.WhenAny(
                saveTask,
                Task.Delay(BlockingObservation, cancellationToken));

            if (completed == saveTask)
            {
                await saveTask;
                throw new InvalidOperationException(
                    "Credential revocation was not blocked by authentication.");
            }

            _gate.RevocationIsBlocked();
            await saveTask;
        }
    }

    private sealed class CountingPrintCodeHasher :
        IPrintCodeHasher
    {
        private IPrintCodeHasher? _inner;
        private int _hashCalls;
        private int _verifyCalls;

        public int HashCalls => Volatile.Read(ref _hashCalls);

        public int VerifyCalls => Volatile.Read(ref _verifyCalls);

        public void Initialize(IPrintCodeHasher inner)
        {
            if (Interlocked.CompareExchange(ref _inner, inner, null) is not null)
            {
                throw new InvalidOperationException(
                    "The counting print-code hasher was initialized more than once.");
            }
        }

        public string Hash(string printCode)
        {
            Interlocked.Increment(ref _hashCalls);
            return RequiredInner().Hash(printCode);
        }

        public bool Verify(string hash, string printCode)
        {
            Interlocked.Increment(ref _verifyCalls);
            return RequiredInner().Verify(hash, printCode);
        }

        private IPrintCodeHasher RequiredInner() =>
            _inner ?? throw new InvalidOperationException(
                "The counting print-code hasher is not initialized.");
    }

    private sealed class ApiWebApplicationFactory :
        WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(
                configuration =>
                    configuration.AddInMemoryCollection(
                        ApiSettings()));

            return base.CreateHost(builder);
        }
    }
}
