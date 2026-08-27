namespace FuaPay.Web.Modules.Credits.Web.PrintPayments;

public sealed record ReservePrintPaymentRequest(
    Guid ReserveCommandId,
    string? JobUuid,
    PrintPaymentUserIdentityRequest? UserIdentity,
    long AmountMinorUnits,
    string? Currency);

public sealed record PrintPaymentUserIdentityRequest(
    string? Provider,
    string? TenantId,
    string? ObjectId);

public sealed record ResolutionRequiredRequest(
    Guid ResolutionCommandId);

public sealed record TerminalPrintPaymentRequest(
    Guid TerminalCommandId);

public sealed record PrintPaymentReservationResponse(
    Guid ReservationId,
    string JobUuid,
    long AmountMinorUnits,
    string Currency,
    string Status,
    Guid ReserveCommandId,
    Guid? ResolutionCommandId,
    Guid? TerminalCommandId,
    Guid? DebitOperationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset StateChangedAt);
