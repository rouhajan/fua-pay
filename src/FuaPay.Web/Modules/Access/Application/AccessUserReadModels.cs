using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Application;

public sealed record AccessUserListRequest
{
    public const int DefaultLimit = 30;
    public const int MaximumLimit = 100;

    public AccessUserListRequest(
        string? search = null,
        int offset = 0,
        int limit = DefaultLimit)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (limit <= 0 || limit > MaximumLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        Search = string.IsNullOrWhiteSpace(search)
            ? null
            : search.Trim();
        Offset = offset;
        Limit = limit;
    }

    public string? Search { get; }

    public int Offset { get; }

    public int Limit { get; }
}

public sealed record AccessUserListItem(
    Guid Id,
    string DisplayName,
    string? Email,
    AccessUserStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    IReadOnlyList<AccessRole> ActiveRoles);

public sealed record AccessExternalIdentityReadModel(
    string Provider,
    string Tenant,
    string Subject);

public sealed record AccessRoleAssignmentReadModel(
    Guid Id,
    AccessRole Role,
    DateTimeOffset GrantedAt,
    string GrantedBy,
    DateTimeOffset? RevokedAt,
    string? RevokedBy)
{
    public bool IsActive => RevokedAt is null;
}

public sealed record AccessUserDetail(
    Guid Id,
    string DisplayName,
    string? Email,
    AccessUserStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    long Version,
    IReadOnlyList<AccessRole> ActiveRoles,
    IReadOnlyList<AccessExternalIdentityReadModel> ExternalIdentities,
    IReadOnlyList<AccessRoleAssignmentReadModel> RoleAssignments);

public sealed record AccessUserOption(
    Guid Id,
    string DisplayName,
    string? Email);

public sealed record AccessUserPage(
    IReadOnlyList<AccessUserListItem> Items,
    int Offset,
    int Limit,
    long TotalCount)
{
    public bool HasPrevious => Offset > 0;

    public bool HasMore => (long)Offset + Items.Count < TotalCount;
}
