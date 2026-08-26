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

    [Fact]
    public void RequireResolution_TransitionsReservedAndReplaysSameCommand()
    {
        var reservation = CreateReservation();
        var commandId = Guid.NewGuid();
        var changedAt = CreatedAt.AddMinutes(1);

        var changed = reservation.RequireResolution(
            commandId,
            changedAt);
        var replayChanged = reservation.RequireResolution(
            commandId,
            changedAt.AddMinutes(1));

        Assert.True(changed);
        Assert.False(replayChanged);
        Assert.Equal(
            PrintReservationStatus.ResolutionRequired,
            reservation.Status);
        Assert.Equal(commandId, reservation.ResolutionCommandId);
        Assert.Equal(changedAt, reservation.StateChangedAt);
        Assert.Null(reservation.TerminalCommandId);
        Assert.Null(reservation.DebitOperationId);
    }

    [Theory]
    [InlineData(false, "capture")]
    [InlineData(true, "capture")]
    [InlineData(false, "release")]
    [InlineData(true, "release")]
    public void BlockingState_CanTransitionToTerminalState(
        bool resolutionRequired,
        string transition)
    {
        var reservation = CreateReservation();
        var changedAt = CreatedAt.AddMinutes(1);

        if (resolutionRequired)
        {
            _ = reservation.RequireResolution(
                Guid.NewGuid(),
                changedAt);
            changedAt = changedAt.AddMinutes(1);
        }

        var terminalCommandId = Guid.NewGuid();
        var debitOperationId = Guid.NewGuid();

        if (transition == "capture")
        {
            Assert.True(reservation.Capture(
                terminalCommandId,
                debitOperationId,
                changedAt));
            Assert.Equal(
                PrintReservationStatus.Captured,
                reservation.Status);
            Assert.Equal(
                debitOperationId,
                reservation.DebitOperationId);
        }
        else
        {
            Assert.True(reservation.Release(
                terminalCommandId,
                changedAt));
            Assert.Equal(
                PrintReservationStatus.Released,
                reservation.Status);
            Assert.Null(reservation.DebitOperationId);
        }

        Assert.Equal(terminalCommandId, reservation.TerminalCommandId);
        Assert.Equal(changedAt, reservation.StateChangedAt);
    }

    [Theory]
    [InlineData("captured", "release")]
    [InlineData("released", "capture")]
    [InlineData("captured", "capture")]
    [InlineData("released", "release")]
    public void TerminalState_RejectsAnotherTerminalTransition(
        string currentState,
        string attemptedTransition)
    {
        var reservation = CreateReservation();
        var firstCommandId = Guid.NewGuid();

        if (currentState == "captured")
        {
            _ = reservation.Capture(
                firstCommandId,
                Guid.NewGuid(),
                CreatedAt.AddMinutes(1));
        }
        else
        {
            _ = reservation.Release(
                firstCommandId,
                CreatedAt.AddMinutes(1));
        }

        Assert.Throws<InvalidPrintReservationStateTransitionException>(
            () =>
            {
                if (attemptedTransition == "capture")
                {
                    _ = reservation.Capture(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        CreatedAt.AddMinutes(2));
                }
                else
                {
                    _ = reservation.Release(
                        Guid.NewGuid(),
                        CreatedAt.AddMinutes(2));
                }
            });
    }

    [Theory]
    [InlineData("resolution-command")]
    [InlineData("terminal-command")]
    [InlineData("debit-operation")]
    public void Transition_RejectsEmptyCommandIdentifiers(string emptyValue)
    {
        var reservation = CreateReservation();

        Assert.Throws<ArgumentException>(
            () =>
            {
                if (emptyValue == "resolution-command")
                {
                    _ = reservation.RequireResolution(
                        Guid.Empty,
                        CreatedAt.AddMinutes(1));
                    return;
                }

                _ = reservation.Capture(
                    emptyValue == "terminal-command"
                        ? Guid.Empty
                        : Guid.NewGuid(),
                    emptyValue == "debit-operation"
                        ? Guid.Empty
                        : Guid.NewGuid(),
                    CreatedAt.AddMinutes(1));
            });
    }

    [Fact]
    public void Transition_RejectsMissingOrRegressingTimestamp()
    {
        var reservation = CreateReservation();

        Assert.Throws<ArgumentException>(
            () => reservation.Release(Guid.NewGuid(), default));
        Assert.Throws<ArgumentException>(
            () => reservation.Release(
                Guid.NewGuid(),
                CreatedAt.AddTicks(-1)));
    }

    private static PrintReservation CreateReservation()
    {
        return new PrintReservation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"urn:uuid:{Guid.NewGuid():D}",
            new Money(1_250),
            Guid.NewGuid(),
            CreatedAt);
    }

    private static Guid ToGuid(int marker) =>
        marker == 0 ? Guid.Empty : Guid.NewGuid();
}
