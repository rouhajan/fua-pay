using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Jobs.Application;

namespace FuaPay.Web.Modules.Jobs.Web;

public sealed record JobListPresentation(
    JobListItem Job,
    string ServiceUnitCode,
    string ServiceUnitDisplayName,
    AccessUserOption? Customer,
    AccessUserOption? CreatedBy);

public sealed record JobDetailPresentation(
    JobDetail Job,
    string ServiceUnitCode,
    string ServiceUnitDisplayName,
    AccessUserOption? Customer,
    AccessUserOption? CreatedBy);
