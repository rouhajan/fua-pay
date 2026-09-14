using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class EfPrintCredentialRepository : IPrintCredentialRepository
{
    private readonly FuaPayDbContext _dbContext;
    private readonly Dictionary<PrintCredential, PrintCredentialEntity> _tracked = [];

    public EfPrintCredentialRepository(FuaPayDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PrintCredential?> FindByOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.PrintCredentials
            .SingleOrDefaultAsync(item => item.OwnerId == ownerId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var domain = ToDomain(entity);
        _tracked[domain] = entity;
        return domain;
    }

    public async Task<PrintCredentialAuthenticationCandidate?>
        FindAuthenticationCandidateAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default)
    {
        var credentials = await _dbContext.PrintCredentials
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM credits.print_credentials
                WHERE normalized_email = {normalizedEmail}
                  AND revoked_at IS NULL
                FOR UPDATE
                """)
            .AsNoTracking()
            .Select(item => new { item.OwnerId, item.CodeHash })
            .ToArrayAsync(cancellationToken);

        var matchingUsers = await _dbContext.Database
            .SqlQuery<AccessCandidate>(
                $"""
                SELECT
                    candidate.id AS "Id",
                    candidate.status AS "Status",
                    EXISTS (
                        SELECT 1
                        FROM access.role_assignments AS role
                        WHERE role.user_id = candidate.id
                          AND role.role = {(int)AccessRole.Customer}
                          AND role.revoked_at IS NULL
                    ) AS "IsCustomer"
                FROM access.users AS candidate
                WHERE candidate.email IS NOT NULL
                  AND lower(normalize(btrim(candidate.email), NFKC)) = {normalizedEmail}
                LIMIT 2
                """)
            .ToArrayAsync(cancellationToken);

        if (credentials.Length != 1 || matchingUsers.Length != 1)
        {
            return null;
        }

        var credential = credentials[0];
        var user = matchingUsers[0];
        return new PrintCredentialAuthenticationCandidate(
            credential.OwnerId,
            credential.CodeHash,
            credential.OwnerId == user.Id &&
            user.Status == (int)AccessUserStatus.Active &&
            user.IsCustomer);
    }

    public async Task<long> CountAccessUsersByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Database.SqlQuery<long>(
            $"""
            SELECT count(*)::bigint AS "Value"
            FROM access.users AS candidate
            WHERE candidate.email IS NOT NULL
              AND lower(normalize(btrim(candidate.email), NFKC)) = {normalizedEmail}
            """).SingleAsync(cancellationToken);
    }

    public void Add(PrintCredential credential)
    {
        _dbContext.PrintCredentials.Add(new PrintCredentialEntity
        {
            OwnerId = credential.OwnerId,
            NormalizedEmail = credential.NormalizedEmail,
            CodeHash = credential.CodeHash,
            CreatedAt = credential.CreatedAt,
            ChangedAt = credential.ChangedAt,
            RevokedAt = credential.RevokedAt,
            Version = 1
        });
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        foreach (var pair in _tracked)
        {
            pair.Value.NormalizedEmail = pair.Key.NormalizedEmail;
            pair.Value.CodeHash = pair.Key.CodeHash;
            pair.Value.ChangedAt = pair.Key.ChangedAt;
            pair.Value.RevokedAt = pair.Key.RevokedAt;
        }

        foreach (var entry in _dbContext.ChangeTracker.Entries<PrintCredentialEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                var credential = entry.Entity;
                credential.Version++;
            }
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PrintCredentialConcurrencyException(exception);
        }
        catch (DbUpdateException exception)
            when (IsConstraintViolation(
                exception,
                PrintCredentialConfiguration.OwnerPrimaryKey))
        {
            throw new PrintCredentialConcurrencyException(exception);
        }
        catch (DbUpdateException exception)
            when (IsConstraintViolation(
                exception,
                PrintCredentialConfiguration.NormalizedEmailUniqueConstraint))
        {
            throw new PrintCredentialEmailConflictException(exception);
        }
    }

    private static bool IsConstraintViolation(
        DbUpdateException exception,
        string constraintName) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        } postgresException &&
        string.Equals(
            postgresException.ConstraintName,
            constraintName,
            StringComparison.Ordinal);

    private static PrintCredential ToDomain(PrintCredentialEntity entity)
    {
        var domain = PrintCredential.Restore(
            entity.OwnerId,
            entity.NormalizedEmail,
            entity.CodeHash,
            entity.CreatedAt,
            entity.ChangedAt,
            entity.RevokedAt);

        return domain;
    }

    private sealed record AccessCandidate(
        Guid Id,
        int Status,
        bool IsCustomer);
}
