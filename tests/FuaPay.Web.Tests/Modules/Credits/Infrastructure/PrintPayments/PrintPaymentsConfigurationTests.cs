using FuaPay.Web.Modules.Credits.Infrastructure.PrintPayments;

using Microsoft.Extensions.Configuration;

namespace FuaPay.Web.Tests.Modules.Credits.Infrastructure.PrintPayments;

public sealed class PrintPaymentsConfigurationTests
{
    private const string Digest =
        "0123456789abcdef0123456789abcdef" +
        "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Resolve_DefaultConfigurationIsDisabled()
    {
        var result = PrintPaymentsConfiguration.Resolve(
            Configuration(
                new Dictionary<string, string?>()));

        Assert.False(result.Enabled);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public void Resolve_EnabledConfigurationNormalizesDigest()
    {
        var sourceId = Guid.NewGuid();
        var result = PrintPaymentsConfiguration.Resolve(
            Configuration(
                ValidValues(
                    sourceId,
                    Digest.ToUpperInvariant())));

        Assert.True(result.Enabled);
        var source = Assert.Single(result.Sources);
        Assert.Equal(sourceId, source.PrintSourceId);
        Assert.Equal(Digest, source.CredentialSha256);
    }

    [Fact]
    public void Resolve_EnabledWithoutSourcesFailsStartup()
    {
        Assert.Throws<InvalidOperationException>(
            () => PrintPaymentsConfiguration.Resolve(
                Configuration(
                    new Dictionary<string, string?>
                    {
                        ["PrintPayments:Enabled"] = "true"
                    })));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Resolve_InvalidSourceIdFailsStartup(string sourceId)
    {
        var values = ValidValues(Guid.NewGuid(), Digest);
        values["PrintPayments:Sources:0:PrintSourceId"] = sourceId;

        Assert.Throws<InvalidOperationException>(
            () => PrintPaymentsConfiguration.Resolve(
                Configuration(values)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData(
        "z123456789abcdef0123456789abcdef" +
        "0123456789abcdef0123456789abcdef")]
    public void Resolve_MalformedDigestFailsStartup(string digest)
    {
        var values = ValidValues(Guid.NewGuid(), digest);

        Assert.Throws<InvalidOperationException>(
            () => PrintPaymentsConfiguration.Resolve(
                Configuration(values)));
    }

    [Fact]
    public void Resolve_DuplicateSourceIdFailsStartup()
    {
        var sourceId = Guid.NewGuid();
        var values = ValidValues(sourceId, Digest);
        AddSource(
            values,
            index: 1,
            sourceId,
            new string('a', 64));

        Assert.Throws<InvalidOperationException>(
            () => PrintPaymentsConfiguration.Resolve(
                Configuration(values)));
    }

    [Fact]
    public void Resolve_DuplicateDigestFailsStartup()
    {
        var values = ValidValues(Guid.NewGuid(), Digest);
        AddSource(
            values,
            index: 1,
            Guid.NewGuid(),
            Digest.ToUpperInvariant());

        Assert.Throws<InvalidOperationException>(
            () => PrintPaymentsConfiguration.Resolve(
                Configuration(values)));
    }

    private static Dictionary<string, string?> ValidValues(
        Guid sourceId,
        string digest)
    {
        var values = new Dictionary<string, string?>
        {
            ["PrintPayments:Enabled"] = "true"
        };
        AddSource(values, 0, sourceId, digest);
        return values;
    }

    private static void AddSource(
        Dictionary<string, string?> values,
        int index,
        Guid sourceId,
        string digest)
    {
        values[$"PrintPayments:Sources:{index}:PrintSourceId"] =
            sourceId.ToString("D");
        values[$"PrintPayments:Sources:{index}:CredentialSha256"] =
            digest;
    }

    private static IConfiguration Configuration(
        IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
