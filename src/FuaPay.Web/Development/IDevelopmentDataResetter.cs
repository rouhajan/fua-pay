namespace FuaPay.Web.Development;

public interface IDevelopmentDataResetter
{
    Task ResetAsync(
        CancellationToken cancellationToken = default);
}
