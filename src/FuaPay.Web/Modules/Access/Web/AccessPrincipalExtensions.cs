using System.Security.Claims;

using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Web;

public static class AccessPrincipalExtensions
{
    public static Guid? FindAccessUserId(
        this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var values = principal
            .FindAll(ClaimTypes.NameIdentifier)
            .Select(claim => claim.Value)
            .ToArray();

        if (
            values.Length != 1 ||
            !Guid.TryParse(values[0], out var userId) ||
            userId == Guid.Empty)
        {
            return null;
        }

        return userId;
    }

    public static string? FindAccessEmail(
        this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var values = principal
            .FindAll(ClaimTypes.Email)
            .Select(claim => claim.Value)
            .ToArray();

        return values.Length == 1
            ? values[0]
            : null;
    }

    public static IReadOnlyCollection<AccessRole> FindAccessRoles(
        this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return principal
            .FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Select(
                value =>
                    Enum.TryParse<AccessRole>(
                        value,
                        ignoreCase: false,
                        out var role) &&
                    Enum.IsDefined(
                        typeof(AccessRole),
                        role)
                        ? role
                        : (AccessRole?)null)
            .Where(role => role.HasValue)
            .Select(role => role!.Value)
            .ToHashSet();
    }

    public static bool HasValidAccessSessionClaims(
        this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var identities = principal.Identities.ToArray();

        if (
            identities.Length != 1 ||
            identities[0].IsAuthenticated != true)
        {
            return false;
        }

        if (principal.FindAccessUserId() is null)
        {
            return false;
        }

        var names = principal
            .FindAll(ClaimTypes.Name)
            .Select(claim => claim.Value)
            .ToArray();

        if (
            names.Length != 1 ||
            string.IsNullOrWhiteSpace(names[0]) ||
            names[0].Length > AccessTextLimits.DisplayNameMaxLength)
        {
            return false;
        }

        var emails = principal
            .FindAll(ClaimTypes.Email)
            .Select(claim => claim.Value)
            .ToArray();

        if (
            emails.Length > 1 ||
            emails.Any(
                email =>
                    string.IsNullOrWhiteSpace(email) ||
                    email.Length > AccessTextLimits.EmailMaxLength))
        {
            return false;
        }

        var roleValues = principal
            .FindAll(ClaimTypes.Role)
            .Select(claim => claim.Value)
            .ToArray();

        if (
            roleValues.Distinct(StringComparer.Ordinal).Count() !=
            roleValues.Length)
        {
            return false;
        }

        return roleValues.All(
            value =>
                Enum.TryParse<AccessRole>(
                    value,
                    ignoreCase: false,
                    out var role) &&
                Enum.IsDefined(
                    typeof(AccessRole),
                    role));
    }
}
