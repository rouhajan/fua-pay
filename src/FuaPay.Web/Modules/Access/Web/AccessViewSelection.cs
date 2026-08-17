using System.Security.Claims;

using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Web;

public enum AccessView
{
    Customer = 1,
    Requester = 2,
    Admin = 3
}

public sealed record AccessViewOption(
    AccessView View,
    string Key,
    string Label);

public sealed record AccessViewSelection(
    AccessViewOption Active,
    IReadOnlyList<AccessViewOption> Available);

public static class AccessViewSelector
{
    private static readonly IReadOnlyList<AccessViewDefinition>
        Definitions =
        [
            new(
                new AccessViewOption(
                    AccessView.Customer,
                    "customer",
                    "Zákazník"),
                AccessRole.Customer,
                Priority: 1),
            new(
                new AccessViewOption(
                    AccessView.Requester,
                    "requester",
                    "Zadavatel"),
                AccessRole.Requester,
                Priority: 2),
            new(
                new AccessViewOption(
                    AccessView.Admin,
                    "admin",
                    "Administrace"),
                AccessRole.Admin,
                Priority: 3)
        ];

    public static AccessViewSelection? Select(
        ClaimsPrincipal principal,
        string? requestedKey = null)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var roles = principal.FindAccessRoles();

        var availableDefinitions = Definitions
            .Where(
                definition =>
                    roles.Contains(
                        definition.RequiredRole))
            .ToArray();

        if (availableDefinitions.Length == 0)
        {
            return null;
        }

        var requestedDefinition =
            FindRequestedDefinition(
                availableDefinitions,
                requestedKey);

        var activeDefinition =
            requestedDefinition ??
            availableDefinitions.MaxBy(
                definition => definition.Priority)!;

        return new AccessViewSelection(
            activeDefinition.Option,
            Array.AsReadOnly(
                availableDefinitions
                    .Select(
                        definition => definition.Option)
                    .ToArray()));
    }

    private static AccessViewDefinition? FindRequestedDefinition(
        IEnumerable<AccessViewDefinition> availableDefinitions,
        string? requestedKey)
    {
        if (string.IsNullOrWhiteSpace(requestedKey))
        {
            return null;
        }

        var normalizedKey = requestedKey.Trim();

        return availableDefinitions.SingleOrDefault(
            definition =>
                string.Equals(
                    definition.Option.Key,
                    normalizedKey,
                    StringComparison.OrdinalIgnoreCase));
    }

    private sealed record AccessViewDefinition(
        AccessViewOption Option,
        AccessRole RequiredRole,
        int Priority);
}
