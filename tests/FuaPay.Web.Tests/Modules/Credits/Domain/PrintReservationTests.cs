using FuaPay.Web.Modules.Credits.Domain;

namespace FuaPay.Web.Tests.Modules.Credits.Domain;

public sealed class PrintReservationTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_CreatesCanonicalReservedState()
    {
        var id = Guid.NewGuid();
        var creditAccountId = Guid.NewGuid();
        var printSourceId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var reserveCommandId = Guid.NewGuid();

        var reservation = new PrintReservation(
            id,
            creditAccountId,
            printSourceId,
            $"URN:UUID:{jobId:D}".ToUpperInvariant(),
            new Money(1_250),
            reserveCommandId,
            CreatedAt);

        Assert.Equal(id, reservation.Id);
        Assert.Equal(creditAccountId, reservation.CreditAccountId);
        Assert.Equal(printSourceId, reservation.PrintSourceId);
        Assert.Equal(
            $"urn:uuid:{jobId:D}",
            reservation.JobUuid);
        Assert.Equal(new Money(1_250), reservation.Amount);
        Assert.Equal(
            PrintReservationStatus.Reserved,
            reservation.Status);
        Assert.Equal(
            reserveCommandId,
            reservation.ReserveCommandId);
        Assert.Null(reservation.ResolutionCommandId);
        Assert.Null(reservation.TerminalCommandId);
        Assert.Null(reservation.DebitOperationId);
        Assert.Equal(CreatedAt, reservation.CreatedAt);
        Assert.Equal(CreatedAt, reservation.StateChangedAt);
    }

    [Theory]
    [InlineData(0, 1, 1, 1, 1, false)]
    [InlineData(1, 0, 1, 1, 1, false)]
    [InlineData(1, 1, 0, 1, 1, false)]
    [InlineData(1, 1, 1, 0, 1, false)]
    [InlineData(1, 1, 1, 1, 0, false)]
    [InlineData(1, 1, 1, 1, -1, false)]
    [InlineData(1, 1, 1, 1, 1, true)]
    public void Constructor_RejectsInvalidInput(
        int idMarker,
        int accountMarker,
        int sourceMarker,
        int commandMarker,
        long amountMinorUnits,
        bool defaultCreatedAt)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new PrintReservation(
                    ToGuid(idMarker),
                    ToGuid(accountMarker),
                    ToGuid(sourceMarker),
                    $"urn:uuid:{Guid.NewGuid():D}",
                    new Money(amountMinorUnits),
                    ToGuid(commandMarker),
                    defaultCreatedAt ? default : CreatedAt));
    }

    [Theory]
    [InlineData("46db18ef-2a90-4991-8940-fbdb06c84e50")]
    [InlineData("https://example.test/jobs/46db18ef-2a90-4991-8940-fbdb06c84e50")]
    [InlineData("urn:uuid:not-a-uuid")]
    [InlineData(" urn:uuid:46db18ef-2a90-4991-8940-fbdb06c84e50 ")]
    [InlineData("urn:uuid:00000000-0000-0000-0000-000000000000")]
    public void IppJobUuid_RejectsInvalidValue(string value)
    {
        Assert.Throws<ArgumentException>(
            () => IppJobUuid.Normalize(value));
    }

    private static Guid ToGuid(int marker) =>
        marker == 0 ? Guid.Empty : Guid.NewGuid();
}
