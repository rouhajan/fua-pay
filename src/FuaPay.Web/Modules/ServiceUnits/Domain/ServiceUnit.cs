using System.Text.RegularExpressions;


namespace FuaPay.Web.Modules.ServiceUnits.Domain;

public sealed partial class ServiceUnit
{
    public ServiceUnit(
        Guid id,
        string code,
        string displayName,
        ServiceType defaultServiceType,
        DateTimeOffset createdAt,
        ServiceUnitChangeActor createdBy)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "ID pracoviště nesmí být prázdné.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(createdBy);

        Id = id;
        Code = NormalizeCode(code);
        DisplayName = NormalizeDisplayName(displayName);
        DefaultServiceType = ValidateServiceType(defaultServiceType);
        Status = ServiceUnitStatus.Active;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    public Guid Id { get; }

    public string Code { get; }

    public string DisplayName { get; private set; }

    public ServiceType DefaultServiceType { get; private set; }

    public ServiceUnitStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public ServiceUnitChangeActor CreatedBy { get; }

    public DateTimeOffset? DeactivatedAt { get; private set; }

    public ServiceUnitChangeActor? DeactivatedBy { get; private set; }

    public bool IsActive => Status == ServiceUnitStatus.Active;

    public void UpdateDetails(
        string displayName,
        ServiceType defaultServiceType)
    {
        if (!IsActive)
        {
            throw new InactiveServiceUnitException(Id);
        }

        DisplayName = NormalizeDisplayName(displayName);
        DefaultServiceType = ValidateServiceType(defaultServiceType);
    }

    public void Deactivate(
        DateTimeOffset deactivatedAt,
        ServiceUnitChangeActor deactivatedBy)
    {
        ArgumentNullException.ThrowIfNull(deactivatedBy);

        if (!IsActive)
        {
            throw new InactiveServiceUnitException(Id);
        }

        if (deactivatedAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deactivatedAt),
                "Pracoviště nesmí být deaktivováno před vytvořením.");
        }

        Status = ServiceUnitStatus.Inactive;
        DeactivatedAt = deactivatedAt;
        DeactivatedBy = deactivatedBy;
    }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException(
                "Kód pracoviště nesmí být prázdný.",
                nameof(code));
        }

        var normalized = code.Trim().ToUpperInvariant();

        if (!CodePattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                "Kód pracoviště musí mít 2 až 8 velkých písmen " +
                "nebo číslic.",
                nameof(code));
        }

        return normalized;
    }

    private static string NormalizeDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "Název pracoviště nesmí být prázdný.",
                nameof(displayName));
        }

        var normalized = displayName.Trim();

        if (
            normalized.Length >
            ServiceUnitTextLimits.DisplayNameMaxLength)
        {
            throw new ArgumentException(
                "Název pracoviště je příliš dlouhý.",
                nameof(displayName));
        }

        return normalized;
    }

    private static ServiceType ValidateServiceType(
        ServiceType serviceType)
    {
        if (
            serviceType == ServiceType.Unknown ||
            !Enum.IsDefined(serviceType)
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(serviceType),
                "Výchozí druh služby není podporovaný.");
        }

        return serviceType;
    }

    [GeneratedRegex("^[A-Z0-9]{2,8}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
