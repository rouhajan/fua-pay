using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Access.Development;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Development;

internal sealed class EfDevelopmentDataResetter :
    IDevelopmentDataResetter
{
    private readonly FuaPayDbContext _dbContext;

    public EfDevelopmentDataResetter(
        FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task ResetAsync(
        CancellationToken cancellationToken = default)
    {
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var developmentUserIds =
            await _dbContext.AccessExternalIdentities
                .Where(
                    identity =>
                        identity.Provider ==
                            DevelopmentIdentityProfiles.Provider &&
                        identity.Tenant ==
                            DevelopmentIdentityProfiles.Tenant)
                .Select(identity => identity.UserId)
                .Distinct()
                .ToArrayAsync(cancellationToken);

        var mixedIdentityUserIds =
            await _dbContext.AccessExternalIdentities
                .Where(
                    identity =>
                        developmentUserIds.Contains(identity.UserId) &&
                        (
                            identity.Provider !=
                                DevelopmentIdentityProfiles.Provider ||
                            identity.Tenant !=
                                DevelopmentIdentityProfiles.Tenant
                        ))
                .Select(identity => identity.UserId)
                .Distinct()
                .ToArrayAsync(cancellationToken);

        if (mixedIdentityUserIds.Length > 0)
        {
            throw new InvalidDataException(
                "Vývojový reset odmítl smazat interního uživatele " +
                "propojeného také s jiným poskytovatelem identity.");
        }

        var serviceUnitIds =
            DevelopmentDataScenario.ServiceUnitIds.ToArray();

        var externalServiceUnitUsageExists =
            await _dbContext.Jobs.AnyAsync(
                    job =>
                        serviceUnitIds.Contains(job.ServiceUnitId) &&
                        (
                            !developmentUserIds.Contains(
                                job.CustomerUserId) ||
                            !developmentUserIds.Contains(
                                job.CreatedByUserId)
                        ),
                    cancellationToken) ||
            await _dbContext.ServiceUnitRequesterAssignments.AnyAsync(
                assignment =>
                    serviceUnitIds.Contains(assignment.ServiceUnitId) &&
                    !developmentUserIds.Contains(assignment.UserId),
                cancellationToken);

        if (externalServiceUnitUsageExists)
        {
            throw new InvalidDataException(
                "Vývojový reset odmítl smazat pracoviště použité " +
                "uživatelem mimo vývojový scénář.");
        }

        var jobIds = await _dbContext.Jobs
            .Where(
                job =>
                    developmentUserIds.Contains(job.CustomerUserId) ||
                    developmentUserIds.Contains(job.CreatedByUserId))
            .Select(job => job.Id)
            .ToArrayAsync(cancellationToken);

        var paymentIds = await _dbContext.Payments
            .Where(
                payment =>
                    developmentUserIds.Contains(
                        payment.CustomerUserId) ||
                    (
                        payment.JobId.HasValue &&
                        jobIds.Contains(payment.JobId.Value)
                    ))
            .Select(payment => payment.Id)
            .ToArrayAsync(cancellationToken);

        var accountIds =
            await _dbContext.CreditAccounts
                .Where(
                    account =>
                        developmentUserIds.Contains(
                            account.OwnerId))
                .Select(account => account.Id)
                .ToArrayAsync(cancellationToken);

        var assignmentIds =
            await _dbContext.ServiceUnitRequesterAssignments
                .Where(
                    assignment =>
                        developmentUserIds.Contains(
                            assignment.UserId))
                .Select(assignment => assignment.Id)
                .ToArrayAsync(cancellationToken);

        var externalServiceUnitAdministrationExists =
            await _dbContext.ServiceUnits.AnyAsync(
                    unit =>
                        !serviceUnitIds.Contains(unit.Id) &&
                        (
                            (
                                unit.CreatedByUserId.HasValue &&
                                developmentUserIds.Contains(
                                    unit.CreatedByUserId.Value)
                            ) ||
                            (
                                unit.DeactivatedByUserId.HasValue &&
                                developmentUserIds.Contains(
                                    unit.DeactivatedByUserId.Value)
                            )
                        ),
                    cancellationToken) ||
            await _dbContext.ServiceUnitRequesterAssignments.AnyAsync(
                assignment =>
                    !developmentUserIds.Contains(assignment.UserId) &&
                    (
                        (
                            assignment.GrantedByUserId.HasValue &&
                            developmentUserIds.Contains(
                                assignment.GrantedByUserId.Value)
                        ) ||
                        (
                            assignment.RevokedByUserId.HasValue &&
                            developmentUserIds.Contains(
                                assignment.RevokedByUserId.Value)
                        )
                    ),
                cancellationToken);

        if (externalServiceUnitAdministrationExists)
        {
            throw new InvalidDataException(
                "Vývojový reset odmítl smazat identitu, která je " +
                "auditním aktérem změny pracoviště jiného uživatele.");
        }

        var externalRoleAssignmentExists =
            await _dbContext.AccessRoleAssignments
                .AnyAsync(
                    assignment =>
                        !developmentUserIds.Contains(
                            assignment.UserId) &&
                        (
                            (
                                assignment.GrantedByUserId.HasValue &&
                                developmentUserIds.Contains(
                                    assignment.GrantedByUserId.Value)
                            ) ||
                            (
                                assignment.RevokedByUserId.HasValue &&
                                developmentUserIds.Contains(
                                    assignment.RevokedByUserId.Value)
                            )
                        ),
                    cancellationToken);

        if (externalRoleAssignmentExists)
        {
            throw new InvalidDataException(
                "Vývojový reset odmítl smazat identitu, která je " +
                "auditním aktérem změny role jiného uživatele.");
        }

        var roleAssignmentIds =
            await _dbContext.AccessRoleAssignments
                .Where(
                    assignment =>
                        developmentUserIds.Contains(
                            assignment.UserId))
                .Select(assignment => assignment.Id)
                .ToArrayAsync(cancellationToken);

        var auditEntityIds = developmentUserIds
            .Concat(serviceUnitIds)
            .Concat(jobIds)
            .Concat(paymentIds)
            .Concat(accountIds)
            .Concat(assignmentIds)
            .Concat(roleAssignmentIds)
            .Distinct()
            .Select(id => id.ToString())
            .ToArray();

        await _dbContext.AuditEvents
            .Where(
                audit =>
                    (
                        audit.ActorUserId.HasValue &&
                        developmentUserIds.Contains(
                            audit.ActorUserId.Value)
                    ) ||
                    auditEntityIds.Contains(audit.EntityId))
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.NotificationOutbox
            .Where(
                item =>
                    developmentUserIds.Contains(
                        item.RecipientUserId))
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.Payments
            .Where(payment => paymentIds.Contains(payment.Id))
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.Jobs
            .Where(job => jobIds.Contains(job.Id))
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.JobNumberSequences
            .Where(
                sequence =>
                    serviceUnitIds.Contains(
                        sequence.ServiceUnitId))
            .ExecuteDeleteAsync(cancellationToken);

        if (accountIds.Length > 0)
        {
            await _dbContext.CreditMovements
                .Where(
                    movement =>
                        accountIds.Contains(movement.AccountId))
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.CreditAccounts
                .Where(account => accountIds.Contains(account.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await _dbContext.ServiceUnitRequesterAssignments
            .Where(
                assignment =>
                    assignmentIds.Contains(assignment.Id))
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.ServiceUnits
            .Where(
                unit =>
                    serviceUnitIds.Contains(unit.Id) &&
                    !unit.RequesterAssignments.Any())
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.AccessRoleAssignments
            .Where(
                assignment =>
                    roleAssignmentIds.Contains(assignment.Id))
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.AccessExternalIdentities
            .Where(
                identity =>
                    identity.Provider ==
                        DevelopmentIdentityProfiles.Provider &&
                    identity.Tenant ==
                        DevelopmentIdentityProfiles.Tenant)
            .ExecuteDeleteAsync(cancellationToken);

        await _dbContext.AccessUsers
            .Where(
                user =>
                    developmentUserIds.Contains(user.Id))
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        _dbContext.ChangeTracker.Clear();
    }
}
