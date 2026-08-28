namespace FuaPay.Web.Modules.Payments.Infrastructure.Csob;

internal sealed class CsobCryptographicMaterialStartupValidator :
    IHostedService
{
    private readonly ICsobGatewaySignature _signature;

    public CsobCryptographicMaterialStartupValidator(
        ICsobGatewaySignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        _signature = signature;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = _signature;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
