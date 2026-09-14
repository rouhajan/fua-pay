using FuaPay.Web.BuildingBlocks.Application;
using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public sealed class PrintCredentialService
{
    private readonly IAccessSessionQueries _accessQueries;
    private readonly IPrintCredentialRepository _repository;
    private readonly IPrintCodeHasher _hasher;
    private readonly IApplicationTransaction _transaction;
    private readonly IAuditTrail _auditTrail;
    private readonly TimeProvider _timeProvider;

    public PrintCredentialService(
        IAccessSessionQueries accessQueries,
        IPrintCredentialRepository repository,
        IPrintCodeHasher hasher,
        IApplicationTransaction transaction,
        IAuditTrail auditTrail,
        TimeProvider timeProvider)
    {
        _accessQueries = accessQueries;
        _repository = repository;
        _hasher = hasher;
        _transaction = transaction;
        _auditTrail = auditTrail;
        _timeProvider = timeProvider;
    }

    public async Task<PrintCredentialView> GetAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var identity = await GetEligibleIdentityAsync(ownerId, cancellationToken);
        var normalizedEmail = PrintCredentialEmail.Normalize(identity.Email!);

        if (await _repository.CountAccessUsersByNormalizedEmailAsync(
                normalizedEmail,
                cancellationToken) != 1)
        {
            throw new PrintCredentialUnavailableException();
        }

        var credential = await _repository.FindByOwnerAsync(ownerId, cancellationToken);

        return new PrintCredentialView(
            identity.Email!,
            credential?.IsActive == true &&
                string.Equals(
                    credential.NormalizedEmail,
                    normalizedEmail,
                    StringComparison.Ordinal),
            credential?.ChangedAt);
    }

    public async Task SetAsync(
        Guid ownerId,
        string printCode,
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        if (!PrintCodePolicy.IsValid(printCode))
        {
            throw new ArgumentException("Print code must contain exactly six digits.", nameof(printCode));
        }

        if (!string.Equals(printCode, confirmation, StringComparison.Ordinal))
        {
            throw new ArgumentException("Print-code confirmation does not match.", nameof(confirmation));
        }

        _ = await _transaction.ExecuteAsync(
            async ct =>
            {
                var identity = await GetEligibleIdentityAsync(ownerId, ct);
                var normalizedEmail = PrintCredentialEmail.Normalize(identity.Email!);

                if (await _repository.CountAccessUsersByNormalizedEmailAsync(normalizedEmail, ct) != 1)
                {
                    throw new PrintCredentialUnavailableException();
                }

                var now = _timeProvider.GetUtcNow();
                var hash = _hasher.Hash(printCode);
                var credential = await _repository.FindByOwnerAsync(ownerId, ct);
                var action = credential is null || !credential.IsActive
                    ? "print-credential.configured"
                    : "print-credential.changed";

                if (credential is null)
                {
                    credential = new PrintCredential(ownerId, normalizedEmail, hash, now);
                    _repository.Add(credential);
                }
                else
                {
                    credential.Change(normalizedEmail, hash, now);
                }

                _auditTrail.Stage(AuditEntry.ForUser(
                    ownerId,
                    action,
                    "print-credential",
                    ownerId.ToString(),
                    action.EndsWith("configured", StringComparison.Ordinal)
                        ? "Tiskový kód byl nastaven."
                        : "Tiskový kód byl změněn; předchozí kód již není platný.",
                    now));

                await _repository.SaveAsync(ct);
                return true;
            },
            cancellationToken);
    }

    public async Task RevokeAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        _ = await _transaction.ExecuteAsync(
            async ct =>
            {
                _ = await GetEligibleIdentityAsync(ownerId, ct);
                var credential = await _repository.FindByOwnerAsync(ownerId, ct);

                if (credential?.IsActive != true)
                {
                    return false;
                }

                var now = _timeProvider.GetUtcNow();
                credential.Revoke(now);
                _auditTrail.Stage(AuditEntry.ForUser(
                    ownerId,
                    "print-credential.revoked",
                    "print-credential",
                    ownerId.ToString(),
                    "Tiskový kód byl zneplatněn.",
                    now));
                await _repository.SaveAsync(ct);
                return true;
            },
            cancellationToken);
    }

    private async Task<AccessSessionSnapshot> GetEligibleIdentityAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        if (ownerId == Guid.Empty)
        {
            throw new PrintCredentialUnavailableException();
        }

        var identity = await _accessQueries.FindAsync(ownerId, cancellationToken);

        if (
            identity is null ||
            identity.Status != AccessUserStatus.Active ||
            !identity.Roles.Contains(AccessRole.Customer) ||
            !PrintCredentialEmail.TryNormalize(identity.Email, out _))
        {
            throw new PrintCredentialUnavailableException();
        }

        return identity;
    }
}
