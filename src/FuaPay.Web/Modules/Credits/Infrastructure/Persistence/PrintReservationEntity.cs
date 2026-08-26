namespace FuaPay.Web.Modules.Credits.Infrastructure.Persistence;

internal sealed class PrintReservationEntity
{
    public Guid Id { get; set; }

    public Guid CreditAccountId { get; set; }

    public Guid PrintSourceId { get; set; }

    public string JobUuid { get; set; } = string.Empty;

    public long AmountMinorUnits { get; set; }

    public int Status { get; set; }

    public Guid ReserveCommandId { get; set; }

    public Guid? ResolutionCommandId { get; set; }

    public Guid? TerminalCommandId { get; set; }

    public Guid? DebitOperationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset StateChangedAt { get; set; }

    public long Version { get; set; }
}
