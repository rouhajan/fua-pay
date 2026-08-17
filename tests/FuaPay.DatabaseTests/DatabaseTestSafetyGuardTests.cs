namespace FuaPay.DatabaseTests;

public sealed class DatabaseTestSafetyGuardTests
{
    private const string SafeConnectionString =
        "Host=127.0.0.1;Port=55432;Database=fuapay_test_guard;" +
        "Username=test;Password=unused";

    [Fact]
    public void Validate_WithoutExplicitOptIn_Rejects()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => DatabaseTestSafetyGuard.Validate(
                SafeConnectionString,
                explicitOptIn: null));

        Assert.Contains(
            "FUA_PAY_DATABASE_TESTS_ALLOWED=1",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WithoutConnectionString_Rejects()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => DatabaseTestSafetyGuard.Validate(
                connectionString: null,
                explicitOptIn: "1"));

        Assert.Contains(
            "ConnectionStrings:FuaPay",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RemoteHost_Rejects()
    {
        const string connectionString =
            "Host=db.example.test;Database=fuapay_test_guard;" +
            "Username=test;Password=unused";

        var exception = Assert.Throws<InvalidOperationException>(
            () => DatabaseTestSafetyGuard.Validate(
                connectionString,
                explicitOptIn: "1"));

        Assert.Contains(
            "loopback",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("fuapay_dev")]
    [InlineData("fuapay")]
    [InlineData("fuapay_testproduction")]
    [InlineData("fuapay_auditproduction")]
    public void Validate_UnsafeDatabaseName_Rejects(
        string databaseName)
    {
        var connectionString =
            $"Host=localhost;Database={databaseName};" +
            "Username=test;Password=unused";

        var exception = Assert.Throws<InvalidOperationException>(
            () => DatabaseTestSafetyGuard.Validate(
                connectionString,
                explicitOptIn: "1"));

        Assert.Contains(
            "fuapay_test",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("localhost", "fuapay_test")]
    [InlineData("127.0.0.1", "fuapay_test_local")]
    [InlineData("::1", "fuapay_audit_confirm")]
    public void Validate_ExplicitLocalTestDatabase_Accepts(
        string host,
        string databaseName)
    {
        var connectionString =
            $"Host={host};Database={databaseName};" +
            "Username=test;Password=unused";

        DatabaseTestSafetyGuard.Validate(
            connectionString,
            explicitOptIn: "1");
    }
}
