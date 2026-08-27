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
using FuaPay.Web.Modules.Credits.Web.PrintPayments;

using Microsoft.AspNetCore.Mvc.Testing;
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

    private WebApplicationFactory<Program> CreateApiFactory()
    {
        return new ApiWebApplicationFactory();
    }

    private static IReadOnlyDictionary<string, string?> ApiSettings()
    {
        return new Dictionary<string, string?>
        {
            ["PrintPayments:Enabled"] = "true",
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
