using FuaPay.Web.BuildingBlocks.Auditing;
using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.Payments.Infrastructure.Persistence;

internal sealed class EfCsobPaymentRecoveryRepository :
    ICsobPaymentRecoveryRepository,
    IPaymentReconciliationQueries
{
    private const int MaximumErrorLength = 500;

    private readonly FuaPayDbContext _dbContext;
    private readonly IAuditTrail _auditTrail;

    public EfCsobPaymentRecoveryRepository(
        FuaPayDbContext dbContext,
        IAuditTrail auditTrail)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(auditTrail);
        _dbContext = dbContext;
        _auditTrail = auditTrail;
    }

    public async Task<CsobBrowserReturnObservation?> ScheduleFromReturnAsync(
        string providerReference,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        var normalizedReference = CsobPayId.RequireCanonical(
            providerReference,
            nameof(providerReference));
        ValidateTimestamp(observedAt, nameof(observedAt));

        var payment = await _dbContext.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.Provider == (int)PaymentProvider.Csob &&
                    item.ProviderReference == normalizedReference,
                cancellationToken);

        if (payment is null)
        {
            return null;
        }

        if (payment.Status != (int)PaymentStatus.Pending)
        {
            return new CsobBrowserReturnObservation(
                payment.Id,
                IsFirstObservation: false);
        }

        var inserted = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO payments.csob_payment_reconciliation
            (
                payment_id,
                provider_reference,
                state,
                attempt_count,
                next_attempt_at,
                lease_token,
                lease_expires_at,
                last_attempt_at,
                last_browser_return_at,
                last_gateway_payment_status,
                last_result_code,
                last_error,
                created_at,
                updated_at,
                completed_at,
                version
            )
            VALUES
            (
                {payment.Id},
                {normalizedReference},
                {(int)PaymentReconciliationState.Scheduled},
                0,
                {observedAt},
                NULL,
                NULL,
                NULL,
                {observedAt},
                NULL,
                NULL,
                NULL,
                {observedAt},
                {observedAt},
                NULL,
                1
            )
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken);

        var firstObservation = inserted == 1;

        if (!firstObservation)
        {
            var updated = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE payments.csob_payment_reconciliation
                SET
                    last_browser_return_at = {observedAt},
                    updated_at = GREATEST(updated_at, {observedAt}),
                    version = version + 1
                WHERE
                    payment_id = {payment.Id}
                    AND provider_reference = {normalizedReference}
                    AND last_browser_return_at IS NULL;
                """,
                cancellationToken);

            firstObservation = updated == 1;
        }

        if (!firstObservation)
        {
            var persistedRelationship = await _dbContext.CsobPaymentRecoveries
                .AsNoTracking()
                .Where(item =>
                    item.PaymentId == payment.Id ||
                    item.ProviderReference == normalizedReference)
                .Select(item => new
                {
                    item.PaymentId,
                    item.ProviderReference
                })
                .Take(2)
                .ToArrayAsync(cancellationToken);

            if (
                persistedRelationship.Length != 1 ||
                persistedRelationship[0].PaymentId != payment.Id ||
                !string.Equals(
                    persistedRelationship[0].ProviderReference,
                    normalizedReference,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"CSOB reconciliation for payment '{payment.Id}' " +
                    $"and provider reference '{normalizedReference}' is inconsistent.");
            }
        }

        _dbContext.ChangeTracker.Clear();
        return new CsobBrowserReturnObservation(
            payment.Id,
            IsFirstObservation: firstObservation);
    }

    public async Task<int> ScheduleLongOpenPaymentsAsync(
        DateTimeOffset pendingBefore,
        DateTimeOffset scheduledAt,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateTimestamp(pendingBefore, nameof(pendingBefore));
        ValidateTimestamp(scheduledAt, nameof(scheduledAt));
        ValidateLimit(limit);

        var inserted = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO payments.csob_payment_reconciliation
            (
                payment_id,
                provider_reference,
                state,
                attempt_count,
                next_attempt_at,
                lease_token,
                lease_expires_at,
                last_attempt_at,
                last_browser_return_at,
                last_gateway_payment_status,
                last_result_code,
                last_error,
                created_at,
                updated_at,
                completed_at,
                version
            )
            SELECT
                p.id,
                p.provider_reference,
                {(int)PaymentReconciliationState.Scheduled},
                0,
                {scheduledAt},
                NULL,
                NULL,
                NULL,
                NULL,
                NULL,
                NULL,
                NULL,
                {scheduledAt},
                {scheduledAt},
                NULL,
                1
            FROM payments.payments AS p
            WHERE
                p.provider = {(int)PaymentProvider.Csob}
                AND p.status = {(int)PaymentStatus.Pending}
                AND p.provider_reference IS NOT NULL
                AND p.updated_at <= {pendingBefore}
                AND NOT EXISTS
                (
                    SELECT 1
                    FROM payments.csob_payment_reconciliation AS r
                    WHERE r.payment_id = p.id
                )
            ORDER BY p.updated_at, p.id
            LIMIT {limit}
            ON CONFLICT (payment_id) DO NOTHING;
            """,
            cancellationToken);

        _dbContext.ChangeTracker.Clear();
        return inserted;
    }

    public Task<int> RegisterStaleInProgressAsync(
        DateTimeOffset staleBefore,
        DateTimeOffset observedAt,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateTimestamp(staleBefore, nameof(staleBefore));
        ValidateTimestamp(observedAt, nameof(observedAt));
        ValidateLimit(limit);

        const string staleReason =
            "Persistovaná ČSOB inicializace zůstala po crash window ve " +
            "stavu InProgress; automatický payment/init retry je zakázán.";

        return ExecuteAuditedBatchAsync(
            async ct =>
            {
                var stale = await _dbContext.PaymentInitiations
                    .FromSqlInterpolated(
                        $"""
                        SELECT i.*
                        FROM payments.payment_initiations AS i
                        INNER JOIN payments.payments AS p
                            ON p.id = i.payment_id
                        WHERE
                            i.provider = {(int)PaymentProvider.Csob}
                            AND i.state = {(int)PaymentInitiationState.InProgress}
                            AND i.updated_at <= {staleBefore}
                            AND p.provider = {(int)PaymentProvider.Csob}
                            AND p.status = {(int)PaymentStatus.Created}
                        ORDER BY i.updated_at, i.payment_id
                        LIMIT {limit}
                        FOR UPDATE OF i SKIP LOCKED
                        """)
                    .ToListAsync(ct);

                foreach (var initiation in stale)
                {
                    var transitionAt = Max(
                        initiation.UpdatedAt,
                        observedAt);
                    initiation.State = (int)PaymentInitiationState.Uncertain;
                    initiation.LastError = staleReason;
                    initiation.FinishedAt = transitionAt;
                    initiation.UpdatedAt = transitionAt;
                    initiation.Version = checked(initiation.Version + 1);

                    StageRecoveryAudit(
                        initiation.PaymentId,
                        "payment.provider-initiation.stale",
                        $"Stará InProgress inicializace platby " +
                        $"{initiation.PaymentId} byla fail-closed změněna " +
                        "na Uncertain; payment/init se nebude opakovat.",
                        observedAt);
                }

                return stale.Count;
            },
            cancellationToken);
    }

    public Task<int> ScheduleRecoverableUncertainAsync(
        DateTimeOffset scheduledAt,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateTimestamp(scheduledAt, nameof(scheduledAt));
        ValidateLimit(limit);

        return ExecuteAuditedBatchAsync(
            async ct =>
            {
                var candidates = await _dbContext.PaymentInitiations
                    .FromSqlInterpolated(
                        $"""
                        SELECT i.*
                        FROM payments.payment_initiations AS i
                        INNER JOIN payments.payments AS p
                            ON p.id = i.payment_id
                        LEFT JOIN payments.csob_payment_reconciliation AS r
                            ON r.payment_id = i.payment_id
                        WHERE
                            i.provider = {(int)PaymentProvider.Csob}
                            AND i.state = {(int)PaymentInitiationState.Uncertain}
                            AND i.observed_provider_reference IS NOT NULL
                            AND p.provider = {(int)PaymentProvider.Csob}
                            AND p.status = {(int)PaymentStatus.Created}
                            AND
                            (
                                r.payment_id IS NULL
                                OR
                                (
                                    r.state = {(int)PaymentReconciliationState.RequiresAttention}
                                    AND r.provider_reference IS NULL
                                )
                            )
                        ORDER BY i.updated_at, i.payment_id
                        LIMIT {limit}
                        FOR UPDATE OF i SKIP LOCKED
                        """)
                    .AsNoTracking()
                    .ToListAsync(ct);

                foreach (var initiation in candidates)
                {
                    var reference = initiation.ObservedProviderReference
                        ?? throw new InvalidDataException(
                            $"Nejasná inicializace '{initiation.PaymentId}' nemá candidate payId.");
                    var recovery = await _dbContext.CsobPaymentRecoveries
                        .SingleOrDefaultAsync(
                            item => item.PaymentId == initiation.PaymentId,
                            ct);

                    if (recovery is null)
                    {
                        _dbContext.CsobPaymentRecoveries.Add(
                            CreateRecovery(
                                initiation.PaymentId,
                                reference,
                                PaymentReconciliationState.Scheduled,
                                scheduledAt,
                                error: null));
                    }
                    else
                    {
                        recovery.ProviderReference = reference;
                        recovery.State =
                            (int)PaymentReconciliationState.Scheduled;
                        recovery.NextAttemptAt = scheduledAt;
                        recovery.LastError = null;
                        recovery.UpdatedAt = Max(
                            recovery.UpdatedAt,
                            scheduledAt);
                        recovery.Version = checked(recovery.Version + 1);
                    }

                    StageRecoveryAudit(
                        initiation.PaymentId,
                        "payment.provider-initiation.verification-scheduled",
                        $"Candidate payId {reference} platby " +
                        $"{initiation.PaymentId} byl zařazen do lease queue " +
                        "pro payment/status; stav platby zůstal Created.",
                        scheduledAt);
                }

                return candidates.Count;
            },
            cancellationToken);
    }

    public Task<int> RegisterUnrecoverableUncertainAsync(
        DateTimeOffset observedAt,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateTimestamp(observedAt, nameof(observedAt));
        ValidateLimit(limit);

        const string attentionReason =
            "Inicializace ČSOB je nejasná a nemá bezpečně známé payId; " +
            "automatický payment/init retry je zakázán.";

        return ExecuteAuditedBatchAsync(
            async ct =>
            {
                var candidates = await _dbContext.PaymentInitiations
                    .FromSqlInterpolated(
                        $"""
                        SELECT i.*
                        FROM payments.payment_initiations AS i
                        INNER JOIN payments.payments AS p
                            ON p.id = i.payment_id
                        WHERE
                            p.provider = {(int)PaymentProvider.Csob}
                            AND p.status = {(int)PaymentStatus.Created}
                            AND i.provider = {(int)PaymentProvider.Csob}
                            AND i.state = {(int)PaymentInitiationState.Uncertain}
                            AND i.observed_provider_reference IS NULL
                            AND NOT EXISTS
                            (
                                SELECT 1
                                FROM payments.csob_payment_reconciliation AS r
                                WHERE r.payment_id = p.id
                            )
                        ORDER BY i.updated_at, i.payment_id
                        LIMIT {limit}
                        FOR UPDATE OF i SKIP LOCKED
                        """)
                    .AsNoTracking()
                    .ToListAsync(ct);

                foreach (var initiation in candidates)
                {
                    _dbContext.CsobPaymentRecoveries.Add(
                        CreateRecovery(
                            initiation.PaymentId,
                            providerReference: null,
                            PaymentReconciliationState.RequiresAttention,
                            observedAt,
                            attentionReason));
                    StageRecoveryAudit(
                        initiation.PaymentId,
                        "payment.provider-initiation.requires-attention",
                        $"Nejasná inicializace platby {initiation.PaymentId} " +
                        "nemá bezpečně známé payId a vyžaduje ruční zásah; " +
                        "payment/init se nebude opakovat.",
                        observedAt);
                }

                return candidates.Count;
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<CsobPaymentRecoveryClaim>> ClaimDueAsync(
        DateTimeOffset now,
        TimeSpan leaseDuration,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateTimestamp(now, nameof(now));
        ValidatePositiveDuration(leaseDuration, nameof(leaseDuration));
        ValidateLimit(limit);

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        var due = await _dbContext.CsobPaymentRecoveries
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM payments.csob_payment_reconciliation
                WHERE
                    (
                        state = {(int)PaymentReconciliationState.Scheduled}
                        OR state = {(int)PaymentReconciliationState.Leased}
                    )
                    AND next_attempt_at <= {now}
                    AND
                    (
                        lease_expires_at IS NULL
                        OR lease_expires_at <= {now}
                    )
                ORDER BY next_attempt_at, created_at, payment_id
                LIMIT {limit}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        var leaseExpiresAt = now + leaseDuration;
        var claims = new List<CsobPaymentRecoveryClaim>(due.Count);

        foreach (var entity in due)
        {
            var leaseToken = Guid.NewGuid();
            entity.State = (int)PaymentReconciliationState.Leased;
            entity.LeaseToken = leaseToken;
            entity.LeaseExpiresAt = leaseExpiresAt;
            entity.UpdatedAt = Max(entity.UpdatedAt, now);
            entity.Version = checked(entity.Version + 1);

            claims.Add(new CsobPaymentRecoveryClaim(
                entity.PaymentId,
                entity.ProviderReference
                    ?? throw new InvalidDataException(
                        $"Leased reconciliation '{entity.PaymentId}' nemá provider reference."),
                entity.AttemptCount,
                leaseToken));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear();

        return claims;
    }

    public Task<bool> RescheduleAsync(
        CsobPaymentRecoveryClaim claim,
        DateTimeOffset attemptedAt,
        DateTimeOffset nextAttemptAt,
        int? gatewayPaymentStatus,
        int? resultCode,
        string? error,
        CancellationToken cancellationToken = default)
    {
        if (nextAttemptAt < attemptedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextAttemptAt),
                "Další pokus nesmí předcházet právě dokončenému pokusu.");
        }

        return TransitionClaimAsync(
            claim,
            PaymentReconciliationState.Scheduled,
            attemptedAt,
            nextAttemptAt,
            gatewayPaymentStatus,
            resultCode,
            error,
            completedAt: null,
            cancellationToken);
    }

    public Task<bool> MarkRequiresAttentionAsync(
        CsobPaymentRecoveryClaim claim,
        DateTimeOffset attemptedAt,
        int? gatewayPaymentStatus,
        int? resultCode,
        string error,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException(
                "Důvod ručního zásahu nesmí být prázdný.",
                nameof(error));
        }

        return TransitionClaimAsync(
            claim,
            PaymentReconciliationState.RequiresAttention,
            attemptedAt,
            attemptedAt,
            gatewayPaymentStatus,
            resultCode,
            error,
            completedAt: null,
            cancellationToken);
    }

    public Task<bool> MarkCompletedAsync(
        CsobPaymentRecoveryClaim claim,
        DateTimeOffset attemptedAt,
        int gatewayPaymentStatus,
        int resultCode,
        CancellationToken cancellationToken = default)
    {
        return TransitionClaimAsync(
            claim,
            PaymentReconciliationState.Completed,
            attemptedAt,
            attemptedAt,
            gatewayPaymentStatus,
            resultCode,
            error: null,
            completedAt: attemptedAt,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentReconciliationAdminItem>> ListOpenAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ValidateLimit(limit);

        var openStates = new[]
        {
            (int)PaymentReconciliationState.Scheduled,
            (int)PaymentReconciliationState.Leased,
            (int)PaymentReconciliationState.RequiresAttention
        };

        return await (
            from recovery in _dbContext.CsobPaymentRecoveries.AsNoTracking()
            join initiation in _dbContext.PaymentInitiations.AsNoTracking()
                on recovery.PaymentId equals initiation.PaymentId
                into initiations
            from initiation in initiations.DefaultIfEmpty()
            where openStates.Contains(recovery.State)
            orderby
                recovery.State == (int)PaymentReconciliationState.RequiresAttention
                    descending,
                recovery.NextAttemptAt,
                recovery.PaymentId
            select new PaymentReconciliationAdminItem(
                recovery.PaymentId,
                PaymentProvider.Csob,
                recovery.ProviderReference,
                initiation == null ? null : initiation.CorrelationId,
                (PaymentReconciliationState)recovery.State,
                recovery.AttemptCount,
                recovery.NextAttemptAt,
                recovery.LeaseExpiresAt,
                recovery.LastAttemptAt,
                recovery.LastBrowserReturnAt,
                recovery.LastGatewayPaymentStatus,
                recovery.LastResultCode,
                recovery.LastError,
                recovery.UpdatedAt))
            .Take(limit)
            .ToArrayAsync(cancellationToken);
    }

    private async Task<bool> TransitionClaimAsync(
        CsobPaymentRecoveryClaim claim,
        PaymentReconciliationState targetState,
        DateTimeOffset attemptedAt,
        DateTimeOffset nextAttemptAt,
        int? gatewayPaymentStatus,
        int? resultCode,
        string? error,
        DateTimeOffset? completedAt,
        CancellationToken cancellationToken)
    {
        ValidateClaim(claim);
        ValidateTimestamp(attemptedAt, nameof(attemptedAt));

        var entity = await _dbContext.CsobPaymentRecoveries
            .SingleOrDefaultAsync(
                item =>
                    item.PaymentId == claim.PaymentId &&
                    item.State == (int)PaymentReconciliationState.Leased &&
                    item.LeaseToken == claim.LeaseToken,
                cancellationToken);

        if (entity is null)
        {
            return false;
        }

        entity.State = (int)targetState;
        entity.AttemptCount = checked(entity.AttemptCount + 1);
        entity.NextAttemptAt = nextAttemptAt;
        entity.LeaseToken = null;
        entity.LeaseExpiresAt = null;
        entity.LastAttemptAt = attemptedAt;
        entity.LastGatewayPaymentStatus = gatewayPaymentStatus;
        entity.LastResultCode = resultCode;
        entity.LastError = NormalizeError(error);
        entity.UpdatedAt = Max(entity.UpdatedAt, attemptedAt);
        entity.CompletedAt = completedAt;
        entity.Version = checked(entity.Version + 1);

        await SaveAndClearAsync(cancellationToken);
        return true;
    }

    private async Task<int> ExecuteAuditedBatchAsync(
        Func<CancellationToken, Task<int>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        try
        {
            var affected = await operation(cancellationToken);
            await SaveAndClearAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return affected;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                _dbContext.ChangeTracker.Clear();
            }

            throw;
        }
    }

    private static CsobPaymentRecoveryEntity CreateRecovery(
        Guid paymentId,
        string? providerReference,
        PaymentReconciliationState state,
        DateTimeOffset createdAt,
        string? error)
    {
        return new CsobPaymentRecoveryEntity
        {
            PaymentId = paymentId,
            ProviderReference = providerReference,
            State = (int)state,
            AttemptCount = 0,
            NextAttemptAt = createdAt,
            LeaseToken = null,
            LeaseExpiresAt = null,
            LastAttemptAt = null,
            LastBrowserReturnAt = null,
            LastGatewayPaymentStatus = null,
            LastResultCode = null,
            LastError = error,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            CompletedAt = null,
            Version = 1
        };
    }

    private void StageRecoveryAudit(
        Guid paymentId,
        string action,
        string description,
        DateTimeOffset occurredAt)
    {
        _auditTrail.Stage(AuditEntry.ForProcess(
            "payment-reconciliation",
            action,
            "payment",
            paymentId.ToString(),
            description,
            occurredAt));
    }

    private async Task SaveAndClearAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }
        catch
        {
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        _dbContext.ChangeTracker.Clear();
    }

    private static string? NormalizeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        var normalized = error.Trim();
        return normalized.Length <= MaximumErrorLength
            ? normalized
            : normalized[..MaximumErrorLength];
    }

    private static DateTimeOffset Max(
        DateTimeOffset first,
        DateTimeOffset second) =>
        first >= second ? first : second;

    private static void ValidateClaim(
        CsobPaymentRecoveryClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);

        if (claim.PaymentId == Guid.Empty || claim.LeaseToken == Guid.Empty)
        {
            throw new ArgumentException(
                "Reconciliation claim není platný.",
                nameof(claim));
        }
    }

    private static void ValidateTimestamp(
        DateTimeOffset value,
        string parameterName)
    {
        if (value == default)
        {
            throw new ArgumentException(
                "Čas nesmí být prázdný.",
                parameterName);
        }
    }

    private static void ValidatePositiveDuration(
        TimeSpan value,
        string parameterName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Limit musí být v rozsahu 1 až 100.");
        }
    }
}
