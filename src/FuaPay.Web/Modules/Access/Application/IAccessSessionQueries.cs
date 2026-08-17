namespace FuaPay.Web.Modules.Access.Application;

public interface IAccessSessionQueries
{
    Task<AccessSessionSnapshot?> FindAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
