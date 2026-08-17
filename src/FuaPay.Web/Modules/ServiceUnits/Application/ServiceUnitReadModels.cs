
namespace FuaPay.Web.Modules.ServiceUnits.Application;

public sealed record ServiceUnitReadModel(
    Guid Id,
    string Code,
    string DisplayName,
    ServiceType DefaultServiceType);
