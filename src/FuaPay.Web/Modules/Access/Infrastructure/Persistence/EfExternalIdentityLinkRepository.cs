using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace FuaPay.Web.Modules.Access.Infrastructure.Persistence;

internal sealed class EfExternalIdentityLinkRepository :
    IExternalIdentityLinkRepository
{
    private readonly FuaPayDbContext _dbContext;

    public EfExternalIdentityLinkRepository(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task AttachAsync(
        Guid userId,
        ExternalIdentityKey identityKey,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(identityKey);

        _dbContext.AccessExternalIdentities.Add(
            new ExternalIdentityEntity
            {
                Provider = identityKey.Provider,
                Tenant = identityKey.Tenant,
                Subject = identityKey.Subject,
                UserId = userId
            });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                ExternalIdentityConfiguration.PrimaryKeyConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new ExternalIdentityAlreadyAssignedException(
                identityKey,
                exception);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception,
                ExternalIdentityConfiguration
                    .UserProviderTenantUniqueConstraint))
        {
            _dbContext.ChangeTracker.Clear();
            throw new ExternalIdentityProviderAlreadyAssignedException(
                userId,
                identityKey.Provider,
                identityKey.Tenant,
                exception);
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
    }

    private static bool IsUniqueViolation(
        DbUpdateException exception,
        string constraintName)
    {
        return
            exception.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
            string.Equals(
                postgres.ConstraintName,
                constraintName,
                StringComparison.Ordinal);
    }
}
