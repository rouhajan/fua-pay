using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FuaPay.Web.Tests.Testing;

public sealed class ConfiguredWebApplicationFactory :
    WebApplicationFactory<Program>
{
    private readonly string _environmentName;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    public ConfiguredWebApplicationFactory()
        : this(
            Environments.Development,
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:FuaPay"] =
                    "Host=localhost;Database=unused;" +
                    "Username=unused;Password=unused"
            })
    {
    }

    internal ConfiguredWebApplicationFactory(
        string environmentName,
        IReadOnlyDictionary<string, string?> settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            environmentName);
        ArgumentNullException.ThrowIfNull(settings);

        _environmentName = environmentName;
        _settings = settings;
    }

    protected override IHost CreateHost(
        IHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>(_settings)
        {
            ["Logging:EventLog:LogLevel:Default"] = "None"
        };

        if (
            !settings.ContainsKey("Payments:Provider") &&
            !Environments.Production.Equals(
                _environmentName,
                StringComparison.OrdinalIgnoreCase))
        {
            settings["Payments:Provider"] = "Development";
            if (!Environments.Development.Equals(
                    _environmentName,
                    StringComparison.OrdinalIgnoreCase))
            {
                settings["StagingTestMode:Enabled"] = "true";
                settings["StagingTestMode:SimulatedPaymentsEnabled"] =
                    "true";
            }
        }

        builder.UseEnvironment(_environmentName);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureHostConfiguration(
            configuration =>
                configuration.AddInMemoryCollection(
                    settings));

        return base.CreateHost(builder);
    }
}
