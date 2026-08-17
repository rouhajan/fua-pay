using FuaPay.Web.BuildingBlocks.Domain;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Development;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Development;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuaPay.DatabaseTests;

public sealed class DevelopmentDataPersistenceTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset TestTime =
        new(2026, 7, 28, 14, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public DevelopmentDataPersistenceTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ResetAsync_DeletesDevelopmentIdentityDataOnly()
    {
        var targetOwnerId = Guid.Empty;
        var controlOwnerId = Guid.NewGuid();
        var targetJobId = Guid.NewGuid();
        var controlJobId = Guid.NewGuid();

        using var factory =
            _factory.WithWebHostBuilder(
                builder =>
                    builder.ConfigureServices(
                        services =>
                            services.AddDevelopmentData(
                                enabled: true)));

        try
        {
            using (var seedScope = factory.Services.CreateScope())
            {
                var signInService =
                    seedScope.ServiceProvider
                        .GetRequiredService<DevelopmentSignInService>();

                var profile =
                    Assert.IsType<DevelopmentIdentityProfile>(
                        DevelopmentIdentityProfiles.Find(
                            DevelopmentIdentityProfiles.PrimaryCustomerKey));

                targetOwnerId =
                    (await signInService.ResolveAsync(
                        profile,
                        CancellationToken.None)).Id;

                var creditService =
                    seedScope.ServiceProvider
                        .GetRequiredService<CreditService>();

                var jobRepository =
                    seedScope.ServiceProvider
                        .GetRequiredService<IJobRepository>();

                await creditService.CreditAsync(
                    targetOwnerId,
                    Guid.NewGuid(),
                    Money.FromCrowns(100m),
                    "Cílový vývojový účet");

                await creditService.CreditAsync(
                    controlOwnerId,
                    Guid.NewGuid(),
                    Money.FromCrowns(200m),
                    "Kontrolní vývojový účet");

                await jobRepository.AddAsync(
                    new Job(
                        targetJobId,
                        NextJobNumber(),
                        Guid.NewGuid(),
                        targetOwnerId,
                        targetOwnerId,
                        ServiceType.ThreeDPrint,
                        "Cílová zakázka",
                        "Zakázka určená ke smazání resetem.",
                        Money.FromCrowns(50m),
                        TestTime),
                    CancellationToken.None);

                await jobRepository.AddAsync(
                    new Job(
                        controlJobId,
                        NextJobNumber(),
                        Guid.NewGuid(),
                        controlOwnerId,
                        controlOwnerId,
                        ServiceType.Workshop,
                        "Kontrolní zakázka",
                        "Zakázka, která musí po resetu zůstat.",
                        Money.FromCrowns(75m),
                        TestTime),
                    CancellationToken.None);
            }

            using (var resetScope = factory.Services.CreateScope())
            {
                var resetter =
                    resetScope.ServiceProvider
                        .GetRequiredService<
                            IDevelopmentDataResetter>();

                await resetter.ResetAsync(
                    CancellationToken.None);
            }

            using var verificationScope =
                factory.Services.CreateScope();

            var creditRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<
                        ICreditAccountRepository>();

            var jobVerificationRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<IJobRepository>();

            var accessUserRepository =
                verificationScope.ServiceProvider
                    .GetRequiredService<IAccessUserRepository>();

            Assert.Null(
                await accessUserRepository.FindByIdAsync(
                    targetOwnerId,
                    CancellationToken.None));

            Assert.Null(
                await creditRepository.FindByOwnerIdAsync(
                    targetOwnerId,
                    CancellationToken.None));

            Assert.NotNull(
                await creditRepository.FindByOwnerIdAsync(
                    controlOwnerId,
                    CancellationToken.None));

            Assert.Null(
                await jobVerificationRepository.FindByIdAsync(
                    targetJobId,
                    CancellationToken.None));

            Assert.NotNull(
                await jobVerificationRepository.FindByIdAsync(
                    controlJobId,
                    CancellationToken.None));
        }
        finally
        {
            await DeleteDataAsync(
                factory,
                targetOwnerId,
                controlOwnerId,
                targetJobId,
                controlJobId);
        }
    }


    [Fact]
    public async Task ResetAsync_RefusesUserLinkedToAnotherIdentityProvider()
    {
        var developmentUserId = Guid.Empty;

        using var factory =
            _factory.WithWebHostBuilder(
                builder =>
                    builder.ConfigureServices(
                        services =>
                            services.AddDevelopmentData(
                                enabled: true)));

        try
        {
            using (var seedScope = factory.Services.CreateScope())
            {
                var signInService = seedScope.ServiceProvider
                    .GetRequiredService<DevelopmentSignInService>();
                var dbContext = seedScope.ServiceProvider
                    .GetRequiredService<FuaPayDbContext>();

                var profile =
                    Assert.IsType<DevelopmentIdentityProfile>(
                        DevelopmentIdentityProfiles.Find(
                            DevelopmentIdentityProfiles.PrimaryCustomerKey));

                developmentUserId =
                    (await signInService.ResolveAsync(profile)).Id;

                var externalSubject = Guid.NewGuid().ToString("N");

                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO access.external_identities
                        (provider, tenant, subject, user_id)
                    VALUES
                        ('test-entra', 'tul-test', {externalSubject}, {developmentUserId})
                    """);
            }

            using (var resetScope = factory.Services.CreateScope())
            {
                var resetter = resetScope.ServiceProvider
                    .GetRequiredService<IDevelopmentDataResetter>();

                var exception =
                    await Assert.ThrowsAsync<InvalidDataException>(
                        () => resetter.ResetAsync(
                            CancellationToken.None));

                Assert.Contains(
                    "jiným poskytovatelem identity",
                    exception.Message,
                    StringComparison.Ordinal);
            }

            using var verifyScope = factory.Services.CreateScope();
            var verificationRepository = verifyScope.ServiceProvider
                .GetRequiredService<IAccessUserRepository>();

            Assert.NotNull(
                await verificationRepository.FindByIdAsync(
                    developmentUserId,
                    CancellationToken.None));
        }
        finally
        {
            await DeleteAccessUsersAsync(
                factory,
                developmentUserId);
        }
    }

    [Fact]
    public async Task ResetAsync_RefusesExternalRoleChangesMadeByDevelopmentUser()
    {
        var developmentUserId = Guid.Empty;
        var controlUserId = Guid.Empty;

        using var factory =
            _factory.WithWebHostBuilder(
                builder =>
                    builder.ConfigureServices(
                        services =>
                            services.AddDevelopmentData(
                                enabled: true)));

        try
        {
            using (var seedScope = factory.Services.CreateScope())
            {
                var signInService = seedScope.ServiceProvider
                    .GetRequiredService<DevelopmentSignInService>();
                var identityService = seedScope.ServiceProvider
                    .GetRequiredService<AccessIdentityService>();
                var repository = seedScope.ServiceProvider
                    .GetRequiredService<IAccessUserRepository>();

                var adminProfile =
                    Assert.IsType<DevelopmentIdentityProfile>(
                        DevelopmentIdentityProfiles.Find(
                            DevelopmentIdentityProfiles.AdministratorKey));

                var developmentUser =
                    await signInService.ResolveAsync(adminProfile);

                var controlResolution =
                    await identityService.ResolveAsync(
                        new VerifiedExternalIdentity(
                            new ExternalIdentityKey(
                                "test-entra",
                                "tul-test",
                                Guid.NewGuid().ToString("N")),
                            "Kontrolní uživatel",
                            "control.user@example.invalid"));

                developmentUserId = developmentUser.Id;
                controlUserId = controlResolution.User.Id;

                controlResolution.User.GrantRole(
                    Guid.NewGuid(),
                    AccessRole.Requester,
                    controlResolution.User.CreatedAt.AddMinutes(1),
                    RoleChangeActor.ForUser(developmentUserId));

                await repository.SaveAsync(
                    controlResolution.User,
                    CancellationToken.None);
            }

            using (var resetScope = factory.Services.CreateScope())
            {
                var resetter = resetScope.ServiceProvider
                    .GetRequiredService<IDevelopmentDataResetter>();

                var exception =
                    await Assert.ThrowsAsync<InvalidDataException>(
                        () => resetter.ResetAsync(
                            CancellationToken.None));

                Assert.Contains(
                    "změny role jiného uživatele",
                    exception.Message,
                    StringComparison.Ordinal);
            }

            using var verifyScope = factory.Services.CreateScope();
            var verificationRepository = verifyScope.ServiceProvider
                .GetRequiredService<IAccessUserRepository>();

            Assert.NotNull(
                await verificationRepository.FindByIdAsync(
                    developmentUserId,
                    CancellationToken.None));

            var controlUser = Assert.IsType<AccessUser>(
                await verificationRepository.FindByIdAsync(
                    controlUserId,
                    CancellationToken.None));

            Assert.True(
                controlUser.HasEffectiveRole(AccessRole.Requester));
        }
        finally
        {
            await DeleteAccessUsersAsync(
                factory,
                developmentUserId,
                controlUserId);
        }
    }

    [Fact]
    public async Task ResetAsync_RefusesExternalServiceUnitChangeMadeByDevelopmentUser()
    {
        var developmentUserId = Guid.Empty;
        var controlUserId = Guid.Empty;
        var serviceUnitId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();

        using var factory =
            _factory.WithWebHostBuilder(
                builder =>
                    builder.ConfigureServices(
                        services =>
                            services.AddDevelopmentData(
                                enabled: true)));

        try
        {
            using (var seedScope = factory.Services.CreateScope())
            {
                var signInService = seedScope.ServiceProvider
                    .GetRequiredService<DevelopmentSignInService>();
                var identityService = seedScope.ServiceProvider
                    .GetRequiredService<AccessIdentityService>();
                var serviceUnitRepository = seedScope.ServiceProvider
                    .GetRequiredService<IServiceUnitRepository>();
                var assignmentRepository = seedScope.ServiceProvider
                    .GetRequiredService<
                        IRequesterServiceUnitAssignmentRepository>();

                var adminProfile =
                    Assert.IsType<DevelopmentIdentityProfile>(
                        DevelopmentIdentityProfiles.Find(
                            DevelopmentIdentityProfiles.AdministratorKey));

                var developmentUser =
                    await signInService.ResolveAsync(adminProfile);

                var controlResolution =
                    await identityService.ResolveAsync(
                        new VerifiedExternalIdentity(
                            new ExternalIdentityKey(
                                "test-entra",
                                "tul-test",
                                Guid.NewGuid().ToString("N")),
                            "Kontrolní zadavatel",
                            "control.requester@example.invalid"));

                developmentUserId = developmentUser.Id;
                controlUserId = controlResolution.User.Id;

                await serviceUnitRepository.AddAsync(
                    new ServiceUnit(
                        serviceUnitId,
                        $"T{Guid.NewGuid():N}"[..8].ToUpperInvariant(),
                        "Kontrolní pracoviště",
                        ServiceType.Other,
                        TestTime,
                        ServiceUnitChangeActor.ForProcess("database-test")),
                    CancellationToken.None);

                await assignmentRepository.AddAsync(
                    new RequesterServiceUnitAssignment(
                        assignmentId,
                        serviceUnitId,
                        controlUserId,
                        TestTime.AddMinutes(1),
                        ServiceUnitChangeActor.ForUser(
                            developmentUserId)),
                    CancellationToken.None);
            }

            using (var resetScope = factory.Services.CreateScope())
            {
                var resetter = resetScope.ServiceProvider
                    .GetRequiredService<IDevelopmentDataResetter>();

                var exception =
                    await Assert.ThrowsAsync<InvalidDataException>(
                        () => resetter.ResetAsync(
                            CancellationToken.None));

                Assert.Contains(
                    "změny pracoviště jiného uživatele",
                    exception.Message,
                    StringComparison.Ordinal);
            }

            using var verifyScope = factory.Services.CreateScope();
            var verificationRepository = verifyScope.ServiceProvider
                .GetRequiredService<
                    IRequesterServiceUnitAssignmentRepository>();

            Assert.NotNull(
                await verificationRepository.FindByIdAsync(
                    assignmentId,
                    CancellationToken.None));
        }
        finally
        {
            await DeleteServiceUnitDataAndAccessUsersAsync(
                factory,
                serviceUnitId,
                assignmentId,
                developmentUserId,
                controlUserId);
        }
    }

    [Fact]
    public async Task ResetAsync_RefusesScenarioUnitJobWithExternalCustomer()
    {
        var developmentUserId = Guid.Empty;
        var controlUserId = Guid.Empty;
        var controlJobId = Guid.NewGuid();

        using var factory =
            _factory.WithWebHostBuilder(
                builder =>
                    builder.ConfigureServices(
                        services =>
                            services.AddDevelopmentData(
                                enabled: true)));

        try
        {
            using (var seedScope = factory.Services.CreateScope())
            {
                var signInService = seedScope.ServiceProvider
                    .GetRequiredService<DevelopmentSignInService>();
                var identityService = seedScope.ServiceProvider
                    .GetRequiredService<AccessIdentityService>();
                var jobRepository = seedScope.ServiceProvider
                    .GetRequiredService<IJobRepository>();

                var requesterProfile =
                    Assert.IsType<DevelopmentIdentityProfile>(
                        DevelopmentIdentityProfiles.Find(
                            DevelopmentIdentityProfiles.PlotterRequesterKey));

                developmentUserId =
                    (await signInService.ResolveAsync(
                        requesterProfile,
                        CancellationToken.None)).Id;

                var controlResolution =
                    await identityService.ResolveAsync(
                        new VerifiedExternalIdentity(
                            new ExternalIdentityKey(
                                "test-entra",
                                "tul-test",
                                Guid.NewGuid().ToString("N")),
                            "Externí zákazník pracoviště",
                            "external.customer@example.invalid"));

                controlUserId = controlResolution.User.Id;

                await jobRepository.AddAsync(
                    new Job(
                        controlJobId,
                        NextJobNumber(),
                        DevelopmentDataScenario.PlotterServiceUnitId,
                        controlUserId,
                        developmentUserId,
                        ServiceType.LargeFormatPrint,
                        "Smíšená zakázka pracoviště",
                        "Externí zákazník nesmí být smazán vývojovým resetem.",
                        Money.FromCrowns(90m),
                        TestTime),
                    CancellationToken.None);
            }

            using (var resetScope = factory.Services.CreateScope())
            {
                var resetter = resetScope.ServiceProvider
                    .GetRequiredService<IDevelopmentDataResetter>();

                var exception =
                    await Assert.ThrowsAsync<InvalidDataException>(
                        () => resetter.ResetAsync(
                            CancellationToken.None));

                Assert.Contains(
                    "pracoviště použité uživatelem mimo vývojový scénář",
                    exception.Message,
                    StringComparison.Ordinal);
            }

            using var verifyScope = factory.Services.CreateScope();
            var verificationRepository = verifyScope.ServiceProvider
                .GetRequiredService<IJobRepository>();

            Assert.NotNull(
                await verificationRepository.FindByIdAsync(
                    controlJobId,
                    CancellationToken.None));
        }
        finally
        {
            await DeleteJobAndAccessUsersAsync(
                factory,
                controlJobId,
                developmentUserId,
                controlUserId);
        }
    }

    [Fact]
    public async Task ResetAsync_RefusesServiceUnitUsedOutsideDevelopmentScenario()
    {
        var controlUserId = Guid.Empty;
        var controlJobId = Guid.NewGuid();

        using var factory =
            _factory.WithWebHostBuilder(
                builder =>
                    builder.ConfigureServices(
                        services =>
                            services.AddDevelopmentData(
                                enabled: true)));

        try
        {
            using (var seedScope = factory.Services.CreateScope())
            {
                var identityService = seedScope.ServiceProvider
                    .GetRequiredService<AccessIdentityService>();
                var jobRepository = seedScope.ServiceProvider
                    .GetRequiredService<IJobRepository>();

                var controlResolution =
                    await identityService.ResolveAsync(
                        new VerifiedExternalIdentity(
                            new ExternalIdentityKey(
                                "test-entra",
                                "tul-test",
                                Guid.NewGuid().ToString("N")),
                            "Kontrolní uživatel pracoviště",
                            "control.unit.user@example.invalid"));

                controlUserId = controlResolution.User.Id;

                await jobRepository.AddAsync(
                    new Job(
                        controlJobId,
                        NextJobNumber(),
                        DevelopmentDataScenario.PlotterServiceUnitId,
                        controlUserId,
                        controlUserId,
                        ServiceType.LargeFormatPrint,
                        "Externí zakázka pracoviště",
                        "Zakázka mimo vývojový scénář musí zůstat.",
                        Money.FromCrowns(90m),
                        TestTime),
                    CancellationToken.None);
            }

            using (var resetScope = factory.Services.CreateScope())
            {
                var resetter = resetScope.ServiceProvider
                    .GetRequiredService<IDevelopmentDataResetter>();

                var exception =
                    await Assert.ThrowsAsync<InvalidDataException>(
                        () => resetter.ResetAsync(
                            CancellationToken.None));

                Assert.Contains(
                    "pracoviště použité uživatelem mimo vývojový scénář",
                    exception.Message,
                    StringComparison.Ordinal);
            }

            using var verifyScope = factory.Services.CreateScope();
            var verificationRepository = verifyScope.ServiceProvider
                .GetRequiredService<IJobRepository>();

            Assert.NotNull(
                await verificationRepository.FindByIdAsync(
                    controlJobId,
                    CancellationToken.None));
        }
        finally
        {
            await DeleteJobAndAccessUsersAsync(
                factory,
                controlJobId,
                controlUserId);
        }
    }

    private static async Task DeleteDataAsync(
        WebApplicationFactory<Program> factory,
        Guid targetOwnerId,
        Guid controlOwnerId,
        Guid targetJobId,
        Guid controlJobId)
    {
        using var scope = factory.Services.CreateScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM jobs.jobs
            WHERE id IN ({targetJobId}, {controlJobId})
            """);

        foreach (var ownerId in new[]
        {
            targetOwnerId,
            controlOwnerId
        })
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM credits.movements
                WHERE account_id IN
                (
                    SELECT id
                    FROM credits.accounts
                    WHERE owner_id = {ownerId}
                )
                """);

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM credits.accounts
                WHERE owner_id = {ownerId}
                """);
        }

        await DeleteAccessUsersAsync(factory, targetOwnerId);
    }

    private static async Task DeleteServiceUnitDataAndAccessUsersAsync(
        WebApplicationFactory<Program> factory,
        Guid serviceUnitId,
        Guid assignmentId,
        params Guid[] userIds)
    {
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM service_units.requester_assignments
                WHERE id = {assignmentId}
                """);

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM service_units.units
                WHERE id = {serviceUnitId}
                """);
        }

        await DeleteAccessUsersAsync(factory, userIds);
    }

    private static async Task DeleteJobAndAccessUsersAsync(
        WebApplicationFactory<Program> factory,
        Guid jobId,
        params Guid[] userIds)
    {
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM jobs.jobs
                WHERE id = {jobId}
                """);
        }

        await DeleteAccessUsersAsync(factory, userIds);
    }

    private static async Task DeleteAccessUsersAsync(
        WebApplicationFactory<Program> factory,
        params Guid[] userIds)
    {
        var ids = userIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return;
        }

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<FuaPayDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM access.role_assignments
            WHERE user_id = ANY ({ids})
               OR granted_by_user_id = ANY ({ids})
               OR revoked_by_user_id = ANY ({ids})
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM access.external_identities
            WHERE user_id = ANY ({ids})
            """);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM access.users
            WHERE id = ANY ({ids})
            """);
    }

}
