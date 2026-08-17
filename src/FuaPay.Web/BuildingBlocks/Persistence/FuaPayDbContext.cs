using FuaPay.Web.Modules.Access.Infrastructure.Persistence;
using FuaPay.Web.Modules.Audit.Infrastructure.Persistence;
using FuaPay.Web.Modules.Credits.Infrastructure.Persistence;
using FuaPay.Web.Modules.Jobs.Infrastructure.Persistence;
using FuaPay.Web.Modules.Notifications.Infrastructure.Persistence;
using FuaPay.Web.Modules.Payments.Infrastructure.Persistence;
using FuaPay.Web.Modules.ServiceUnits.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.BuildingBlocks.Persistence;

public sealed class FuaPayDbContext : DbContext
{
    public FuaPayDbContext(
        DbContextOptions<FuaPayDbContext> options)
        : base(options)
    {
    }

    internal DbSet<AuditEventEntity> AuditEvents =>
        Set<AuditEventEntity>();

    internal DbSet<AccessUserEntity> AccessUsers =>
        Set<AccessUserEntity>();

    internal DbSet<ExternalIdentityEntity> AccessExternalIdentities =>
        Set<ExternalIdentityEntity>();

    internal DbSet<RoleAssignmentEntity> AccessRoleAssignments =>
        Set<RoleAssignmentEntity>();

    internal DbSet<CreditAccountEntity> CreditAccounts =>
        Set<CreditAccountEntity>();

    internal DbSet<CreditMovementEntity> CreditMovements =>
        Set<CreditMovementEntity>();

    internal DbSet<CreditAdjustmentCommandEntity>
        CreditAdjustmentCommands =>
            Set<CreditAdjustmentCommandEntity>();

    internal DbSet<JobEntity> Jobs =>
        Set<JobEntity>();

    internal DbSet<JobNumberSequenceEntity> JobNumberSequences =>
        Set<JobNumberSequenceEntity>();

    internal DbSet<PaymentEntity> Payments =>
        Set<PaymentEntity>();

    internal DbSet<PaymentInitiationEntity> PaymentInitiations =>
        Set<PaymentInitiationEntity>();

    internal DbSet<PaymentOrderNumberSequenceEntity>
        PaymentOrderNumberSequences =>
            Set<PaymentOrderNumberSequenceEntity>();

    internal DbSet<CsobPaymentRecoveryEntity> CsobPaymentRecoveries =>
        Set<CsobPaymentRecoveryEntity>();

    internal DbSet<NotificationOutboxEntity> NotificationOutbox =>
        Set<NotificationOutboxEntity>();

    internal DbSet<ServiceUnitEntity> ServiceUnits =>
        Set<ServiceUnitEntity>();

    internal DbSet<RequesterServiceUnitAssignmentEntity>
        ServiceUnitRequesterAssignments =>
            Set<RequesterServiceUnitAssignmentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FuaPayDbContext).Assembly);
    }
}
