using FuaPay.Web.Modules.Access.Development;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

namespace FuaPay.Web.Development;

public sealed class DevelopmentDataSeeder
{
    private readonly DevelopmentSignInService _signInService;
    private readonly ICreditAccountRepository _creditRepository;
    private readonly IJobRepository _jobRepository;
    private readonly IJobNumberAllocator _jobNumberAllocator;
    private readonly IServiceUnitRepository _serviceUnitRepository;
    private readonly IRequesterServiceUnitAssignmentRepository
        _serviceUnitAssignmentRepository;
    private readonly IDevelopmentDataResetter _resetter;

    public DevelopmentDataSeeder(
        DevelopmentSignInService signInService,
        ICreditAccountRepository creditRepository,
        IJobRepository jobRepository,
        IJobNumberAllocator jobNumberAllocator,
        IServiceUnitRepository serviceUnitRepository,
        IRequesterServiceUnitAssignmentRepository
            serviceUnitAssignmentRepository,
        IDevelopmentDataResetter resetter)
    {
        ArgumentNullException.ThrowIfNull(signInService);
        ArgumentNullException.ThrowIfNull(creditRepository);
        ArgumentNullException.ThrowIfNull(jobRepository);
        ArgumentNullException.ThrowIfNull(jobNumberAllocator);
        ArgumentNullException.ThrowIfNull(serviceUnitRepository);
        ArgumentNullException.ThrowIfNull(
            serviceUnitAssignmentRepository);
        ArgumentNullException.ThrowIfNull(resetter);

        _signInService = signInService;
        _creditRepository = creditRepository;
        _jobRepository = jobRepository;
        _jobNumberAllocator = jobNumberAllocator;
        _serviceUnitRepository = serviceUnitRepository;
        _serviceUnitAssignmentRepository =
            serviceUnitAssignmentRepository;
        _resetter = resetter;
    }

    public async Task SeedAsync(
        bool resetBeforeSeed,
        CancellationToken cancellationToken = default)
    {
        if (resetBeforeSeed)
        {
            await _resetter.ResetAsync(cancellationToken);
        }

        var users = await ResolveUsersAsync(cancellationToken);

        await EnsureServiceUnitsAsync(
            users,
            cancellationToken);

        await EnsureCustomerCreditsAsync(
            users,
            cancellationToken);

        await EnsureJobsAsync(
            users,
            cancellationToken);

        await EnsureJobNumberSequencesAsync(
            cancellationToken);
    }

    private async Task<DevelopmentDataUserIds> ResolveUsersAsync(
        CancellationToken cancellationToken)
    {
        return new DevelopmentDataUserIds(
            (await ResolveProfileAsync(
                DevelopmentIdentityProfiles.PrimaryCustomerKey,
                cancellationToken)).Id,
            (await ResolveProfileAsync(
                DevelopmentIdentityProfiles.LowCreditCustomerKey,
                cancellationToken)).Id,
            (await ResolveProfileAsync(
                DevelopmentIdentityProfiles.ThreeDPrintRequesterKey,
                cancellationToken)).Id,
            (await ResolveProfileAsync(
                DevelopmentIdentityProfiles.WorkshopRequesterKey,
                cancellationToken)).Id,
            (await ResolveProfileAsync(
                DevelopmentIdentityProfiles.PlotterRequesterKey,
                cancellationToken)).Id,
            (await ResolveProfileAsync(
                DevelopmentIdentityProfiles.SecretariatRequesterAKey,
                cancellationToken)).Id,
            (await ResolveProfileAsync(
                DevelopmentIdentityProfiles.SecretariatRequesterBKey,
                cancellationToken)).Id,
            (await ResolveProfileAsync(
                DevelopmentIdentityProfiles.SecretariatRequesterCKey,
                cancellationToken)).Id,
            (await ResolveProfileAsync(
                DevelopmentIdentityProfiles.AdministratorKey,
                cancellationToken)).Id);
    }

    private async Task<AccessUser> ResolveProfileAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var profile = DevelopmentIdentityProfiles.Find(key)
            ?? throw new InvalidOperationException(
                $"Vývojový profil '{key}' nebyl nalezen.");

        return await _signInService.ResolveAsync(
            profile,
            cancellationToken);
    }

    private async Task EnsureServiceUnitsAsync(
        DevelopmentDataUserIds users,
        CancellationToken cancellationToken)
    {
        foreach (var expected in
            DevelopmentDataScenario.CreateServiceUnits())
        {
            var existing =
                await _serviceUnitRepository.FindByIdAsync(
                    expected.Id,
                    cancellationToken);

            if (existing is null)
            {
                var sameCode =
                    await _serviceUnitRepository.FindByCodeAsync(
                        expected.Code,
                        cancellationToken);

                if (sameCode is not null)
                {
                    throw new InvalidDataException(
                        $"Kód vývojového pracoviště '{expected.Code}' " +
                        "již používá jiné pracoviště. Spusťte " +
                        "jednorázový reset vývojových dat.");
                }

                await _serviceUnitRepository.AddAsync(
                    expected,
                    cancellationToken);

                continue;
            }

            if (!ServiceUnitsMatch(existing, expected))
            {
                throw new InvalidDataException(
                    $"Vývojové pracoviště '{expected.Id}' " +
                    "neodpovídá aktuálnímu scénáři. Spusťte " +
                    "jednorázový reset vývojových dat.");
            }
        }

        foreach (var expected in
            DevelopmentDataScenario.CreateServiceUnitAssignments(
                users))
        {
            var existing =
                await _serviceUnitAssignmentRepository.FindByIdAsync(
                    expected.Id,
                    cancellationToken);

            if (existing is null)
            {
                var active =
                    await _serviceUnitAssignmentRepository.FindActiveAsync(
                        expected.ServiceUnitId,
                        expected.UserId,
                        cancellationToken);

                if (active is not null)
                {
                    throw new InvalidDataException(
                        "Vývojový zadavatel má k pracovišti jiné " +
                        "aktivní přiřazení. Spusťte jednorázový " +
                        "reset vývojových dat.");
                }

                await _serviceUnitAssignmentRepository.AddAsync(
                    expected,
                    cancellationToken);

                continue;
            }

            if (!AssignmentsMatch(existing, expected))
            {
                throw new InvalidDataException(
                    $"Vývojové přiřazení '{expected.Id}' " +
                    "neodpovídá aktuálnímu scénáři. Spusťte " +
                    "jednorázový reset vývojových dat.");
            }
        }
    }

    private async Task EnsureCustomerCreditsAsync(
        DevelopmentDataUserIds users,
        CancellationToken cancellationToken)
    {
        foreach (var expectedAccount in
            DevelopmentDataScenario.CreateCustomerCreditAccounts(users))
        {
            var account =
                await _creditRepository.FindByOwnerIdAsync(
                    expectedAccount.OwnerId,
                    cancellationToken);

            if (account is null)
            {
                await _creditRepository.AddAsync(
                    expectedAccount,
                    cancellationToken);

                continue;
            }

            var changed = false;

            foreach (var expectedMovement in expectedAccount.Movements)
            {
                var existingMovement = account.Movements
                    .SingleOrDefault(
                        movement =>
                            movement.OperationId ==
                            expectedMovement.OperationId);

                if (existingMovement is not null)
                {
                    EnsureMovementMatches(
                        existingMovement,
                        expectedMovement);

                    continue;
                }

                ApplyMovement(account, expectedMovement);
                changed = true;
            }

            if (changed)
            {
                await _creditRepository.SaveAsync(
                    account,
                    cancellationToken);
            }
        }
    }

    private async Task EnsureJobsAsync(
        DevelopmentDataUserIds users,
        CancellationToken cancellationToken)
    {
        var expectedJobs =
            DevelopmentDataScenario.CreateJobs(users);

        foreach (var expectedJob in expectedJobs)
        {
            var existingJob =
                await _jobRepository.FindByIdAsync(
                    expectedJob.Id,
                    cancellationToken);

            if (existingJob is null)
            {
                await _jobRepository.AddAsync(
                    expectedJob,
                    cancellationToken);

                continue;
            }

            if (!JobsMatch(existingJob, expectedJob))
            {
                throw new InvalidDataException(
                    $"Vývojová zakázka '{expectedJob.Id}' neodpovídá " +
                    "aktuálnímu scénáři. Spusťte jednorázový reset " +
                    "vývojových dat.");
            }
        }
    }

    private async Task EnsureJobNumberSequencesAsync(
        CancellationToken cancellationToken)
    {
        foreach (var sequence in
            DevelopmentDataScenario.JobNumberSequenceMinimums)
        {
            await _jobNumberAllocator.EnsureAtLeastAsync(
                sequence.Key,
                DevelopmentDataScenario.JobNumberYear,
                sequence.Value,
                cancellationToken);
        }
    }

    private static void ApplyMovement(
        CreditAccount account,
        CreditMovement movement)
    {
        switch (movement.Type)
        {
            case CreditMovementType.Credit:
                account.Credit(
                    movement.OperationId,
                    movement.Amount,
                    movement.RecordedAt,
                    movement.Description);
                break;

            case CreditMovementType.Debit:
                account.Debit(
                    movement.OperationId,
                    movement.Amount,
                    movement.RecordedAt,
                    movement.Description);
                break;

            default:
                throw new InvalidDataException(
                    "Vývojový scénář obsahuje neplatný typ " +
                    "kreditního pohybu.");
        }
    }

    private static void EnsureMovementMatches(
        CreditMovement existing,
        CreditMovement expected)
    {
        if (
            existing.Type != expected.Type ||
            existing.Amount != expected.Amount ||
            existing.RecordedAt != expected.RecordedAt ||
            !string.Equals(
                existing.Description,
                expected.Description,
                StringComparison.Ordinal)
        )
        {
            throw new InvalidDataException(
                $"Vývojový kreditní pohyb '{expected.OperationId}' " +
                "neodpovídá aktuálnímu scénáři. Spusťte " +
                "jednorázový reset vývojových dat.");
        }
    }

    private static bool ServiceUnitsMatch(
        ServiceUnit existing,
        ServiceUnit expected)
    {
        return
            existing.Id == expected.Id &&
            string.Equals(
                existing.Code,
                expected.Code,
                StringComparison.Ordinal) &&
            string.Equals(
                existing.DisplayName,
                expected.DisplayName,
                StringComparison.Ordinal) &&
            existing.DefaultServiceType ==
                expected.DefaultServiceType &&
            existing.Status == expected.Status &&
            existing.CreatedAt == expected.CreatedAt &&
            existing.CreatedBy == expected.CreatedBy &&
            existing.DeactivatedAt == expected.DeactivatedAt &&
            existing.DeactivatedBy == expected.DeactivatedBy;
    }

    private static bool AssignmentsMatch(
        RequesterServiceUnitAssignment existing,
        RequesterServiceUnitAssignment expected)
    {
        return
            existing.Id == expected.Id &&
            existing.ServiceUnitId == expected.ServiceUnitId &&
            existing.UserId == expected.UserId &&
            existing.GrantedAt == expected.GrantedAt &&
            existing.GrantedBy == expected.GrantedBy &&
            existing.RevokedAt == expected.RevokedAt &&
            existing.RevokedBy == expected.RevokedBy;
    }

    private static bool JobsMatch(
        Job existing,
        Job expected)
    {
        return
            existing.Id == expected.Id &&
            string.Equals(
                existing.Number,
                expected.Number,
                StringComparison.Ordinal) &&
            existing.ServiceUnitId == expected.ServiceUnitId &&
            existing.CustomerUserId == expected.CustomerUserId &&
            existing.CreatedByUserId == expected.CreatedByUserId &&
            existing.ServiceType == expected.ServiceType &&
            string.Equals(
                existing.Title,
                expected.Title,
                StringComparison.Ordinal) &&
            string.Equals(
                existing.Description,
                expected.Description,
                StringComparison.Ordinal) &&
            existing.Price == expected.Price &&
            existing.ProductionStatus == expected.ProductionStatus &&
            existing.PaymentStatus == expected.PaymentStatus &&
            existing.SettlementType == expected.SettlementType &&
            existing.SettlementReferenceId ==
                expected.SettlementReferenceId &&
            existing.CreatedAt == expected.CreatedAt &&
            existing.PublishedAt == expected.PublishedAt &&
            existing.SettledAt == expected.SettledAt &&
            existing.ProductionStartedAt ==
                expected.ProductionStartedAt &&
            existing.ReadyForPickupAt == expected.ReadyForPickupAt &&
            existing.CompletedAt == expected.CompletedAt &&
            existing.CancelledAt == expected.CancelledAt;
    }
}
