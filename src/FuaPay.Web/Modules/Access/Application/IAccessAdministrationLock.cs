namespace FuaPay.Web.Modules.Access.Application;

public interface IAccessAdministrationLock
{
    Task AcquireAsync(
        CancellationToken cancellationToken = default);
}
