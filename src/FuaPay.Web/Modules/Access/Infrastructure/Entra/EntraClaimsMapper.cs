using System.Security.Claims;

using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Infrastructure.Entra;

public static class EntraClaimsMapper
{
    public static VerifiedExternalIdentity CreateVerifiedIdentity(
        ClaimsPrincipal principal,
        Guid expectedTenantId)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (expectedTenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "Očekávaný Entra tenant nesmí být prázdný.",
                nameof(expectedTenantId));
        }

        var tenantClaim = RequireSingleClaim(principal, "tid");
        var objectClaim = RequireSingleClaim(principal, "oid");

        if (
            !Guid.TryParse(tenantClaim, out var tenantId) ||
            tenantId == Guid.Empty ||
            tenantId != expectedTenantId)
        {
            throw new ArgumentException(
                "Ověřená identita nepatří očekávanému Entra tenantu.",
                nameof(principal));
        }

        if (
            !Guid.TryParse(objectClaim, out var objectId) ||
            objectId == Guid.Empty)
        {
            throw new ArgumentException(
                "Ověřená identita nemá platný Entra object ID.",
                nameof(principal));
        }

        var displayName = OptionalSingleClaim(principal, "name")
            ?? OptionalSingleClaim(principal, "preferred_username")
            ?? objectId.ToString("D");
        var email = OptionalSingleClaim(principal, "email");

        return new VerifiedExternalIdentity(
            ExternalIdentityKey.FromGuidIdentifiers(
                EntraAuthenticationDefaults.ExternalIdentityProvider,
                tenantId.ToString("D"),
                objectId.ToString("D")),
            displayName,
            email);
    }

    private static string RequireSingleClaim(
        ClaimsPrincipal principal,
        string claimType)
    {
        return OptionalSingleClaim(principal, claimType)
            ?? throw new ArgumentException(
                $"Ověřená identita neobsahuje claim '{claimType}'.",
                nameof(principal));
    }

    private static string? OptionalSingleClaim(
        ClaimsPrincipal principal,
        string claimType)
    {
        var values = principal
            .FindAll(claimType)
            .Select(claim => claim.Value)
            .ToArray();

        if (values.Length > 1)
        {
            throw new ArgumentException(
                $"Ověřená identita obsahuje duplicitní claim '{claimType}'.",
                nameof(principal));
        }

        if (values.Length == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(values[0]))
        {
            throw new ArgumentException(
                $"Ověřená identita obsahuje prázdný claim '{claimType}'.",
                nameof(principal));
        }

        return values[0].Trim();
    }
}
