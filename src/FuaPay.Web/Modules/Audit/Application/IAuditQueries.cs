namespace FuaPay.Web.Modules.Audit.Application;

public interface IAuditQueries
{
    Task<AuditPage> ListAsync(
        AuditListFilter filter,
        AuditPageRequest page,
        CancellationToken cancellationToken = default);
}
