using System.Security.Claims;

using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Web;

public sealed class AccessSessionSynchronizer
{
    private readonly IAccessSessionQueries _sessionQueries;

    public AccessSessionSynchronizer(
        IAccessSessionQueries sessionQueries)
    {
        ArgumentNullException.ThrowIfNull(sessionQueries);
        _sessionQueries = sessionQueries;
    }

    public async Task<AccessSessionSynchronizationResult> SynchronizeAsync(
        ClaimsPrincipal principal,
        string authenticationType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (string.IsNullOrWhiteSpace(authenticationType))
        {
            throw new ArgumentException(
                "Authentication type must not be empty.",
                nameof(authenticationType));
        }

        if (!principal.HasValidAccessSessionClaims())
        {
            return AccessSessionSynchronizationResult.Invalid;
        }

        var userId = principal.FindAccessUserId()!.Value;

        var snapshot = await _sessionQueries.FindAsync(
            userId,
            cancellationToken);

        if (
            snapshot is null ||
            snapshot.Status != AccessUserStatus.Active)
        {
            return AccessSessionSynchronizationResult.Invalid;
        }

        var refreshedPrincipal =
            AccessClaimsPrincipalFactory.Create(
                snapshot,
                authenticationType);

        var shouldRenew =
            !HasSameAccessClaims(
                principal,
                refreshedPrincipal);

        return new AccessSessionSynchronizationResult(
            true,
            shouldRenew,
            refreshedPrincipal);
    }

    private static bool HasSameAccessClaims(
        ClaimsPrincipal current,
        ClaimsPrincipal refreshed)
    {
        if (
            current.FindAccessUserId() !=
            refreshed.FindAccessUserId())
        {
            return false;
        }

        if (!string.Equals(
                current.Identity?.Name,
                refreshed.Identity?.Name,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(
                current.FindAccessEmail(),
                refreshed.FindAccessEmail(),
                StringComparison.Ordinal))
        {
            return false;
        }

        return current
            .FindAccessRoles()
            .SetEquals(refreshed.FindAccessRoles());
    }
}

public sealed record AccessSessionSynchronizationResult(
    bool IsValid,
    bool ShouldRenew,
    ClaimsPrincipal? Principal)
{
    public static AccessSessionSynchronizationResult Invalid { get; } =
        new(
            false,
            false,
            null);
}

internal static class AccessRoleSetExtensions
{
    public static bool SetEquals(
        this IReadOnlyCollection<AccessRole> left,
        IReadOnlyCollection<AccessRole> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return left.Count == right.Count &&
            left.ToHashSet().SetEquals(right);
    }
}
