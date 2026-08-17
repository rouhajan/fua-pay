namespace FuaPay.Web.Modules.Jobs.Application;

public enum JobManagementScope
{
    Unknown = 0,
    AssignedServiceUnits = 1,
    All = 2
}

public sealed record JobManagementActor
{
    private readonly HashSet<Guid> _serviceUnitIds;

    public JobManagementActor(
        Guid userId,
        IEnumerable<Guid> serviceUnitIds)
        : this(
            userId,
            JobManagementScope.AssignedServiceUnits,
            serviceUnitIds)
    {
    }

    public JobManagementActor(
        Guid userId,
        JobManagementScope scope)
        : this(userId, scope, [])
    {
    }

    private JobManagementActor(
        Guid userId,
        JobManagementScope scope,
        IEnumerable<Guid> serviceUnitIds)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID uživatele nesmí být prázdné.",
                nameof(userId));
        }

        if (
            scope == JobManagementScope.Unknown ||
            !Enum.IsDefined(scope)
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(scope),
                "Rozsah správy zakázek není podporovaný.");
        }

        ArgumentNullException.ThrowIfNull(serviceUnitIds);

        _serviceUnitIds = serviceUnitIds.ToHashSet();

        if (_serviceUnitIds.Contains(Guid.Empty))
        {
            throw new ArgumentException(
                "Rozsah pracovišť nesmí obsahovat prázdné ID.",
                nameof(serviceUnitIds));
        }

        UserId = userId;
        Scope = scope;
    }

    public Guid UserId { get; }

    public JobManagementScope Scope { get; }

    public IReadOnlySet<Guid> ServiceUnitIds => _serviceUnitIds;

    public bool CanManage(Guid serviceUnitId)
    {
        if (serviceUnitId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID pracoviště nesmí být prázdné.",
                nameof(serviceUnitId));
        }

        return
            Scope == JobManagementScope.All ||
            _serviceUnitIds.Contains(serviceUnitId);
    }
}
