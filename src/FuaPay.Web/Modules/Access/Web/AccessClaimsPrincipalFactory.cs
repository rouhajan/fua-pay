using System.Security.Claims;

using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Web;

public static class AccessClaimsPrincipalFactory
{
    public static ClaimsPrincipal Create(
        AccessUser user,
        string authenticationType)
    {
        ArgumentNullException.ThrowIfNull(user);

        return Create(
            user.Id,
            user.DisplayName,
            user.Email,
            user.AssignedRoles,
            authenticationType);
    }

    public static ClaimsPrincipal Create(
        AccessSessionSnapshot snapshot,
        string authenticationType)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return Create(
            snapshot.UserId,
            snapshot.DisplayName,
            snapshot.Email,
            snapshot.Roles,
            authenticationType);
    }

    private static ClaimsPrincipal Create(
        Guid userId,
        string displayName,
        string? email,
        IEnumerable<AccessRole> roles,
        string authenticationType)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "Zobrazované jméno nesmí být prázdné.",
                nameof(displayName));
        }

        ArgumentNullException.ThrowIfNull(roles);

        if (string.IsNullOrWhiteSpace(authenticationType))
        {
            throw new ArgumentException(
                "Authentication type must not be empty.",
                nameof(authenticationType));
        }

        var effectiveRoles = roles
            .Distinct()
            .OrderBy(role => role)
            .ToArray();

        if (effectiveRoles.Any(role => !Enum.IsDefined(role)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(roles),
                "Aplikační role není platná.");
        }

        var claims = new List<Claim>
        {
            new(
                ClaimTypes.NameIdentifier,
                userId.ToString()),
            new(
                ClaimTypes.Name,
                displayName)
        };

        if (email is not null)
        {
            claims.Add(
                new Claim(
                    ClaimTypes.Email,
                    email));
        }

        claims.AddRange(
            effectiveRoles.Select(
                role =>
                    new Claim(
                        ClaimTypes.Role,
                        role.ToString())));

        return new ClaimsPrincipal(
            new ClaimsIdentity(
                claims,
                authenticationType,
                ClaimTypes.Name,
                ClaimTypes.Role));
    }
}
