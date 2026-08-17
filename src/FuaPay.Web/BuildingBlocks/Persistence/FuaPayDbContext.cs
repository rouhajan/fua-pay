using FuaPay.Web.Modules.Access.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.BuildingBlocks.Persistence;

public sealed class FuaPayDbContext : DbContext
{
    public FuaPayDbContext(
        DbContextOptions<FuaPayDbContext> options)
        : base(options)
    {
    }

    internal DbSet<AccessUserEntity> AccessUsers =>
        Set<AccessUserEntity>();

    internal DbSet<ExternalIdentityEntity> AccessExternalIdentities =>
        Set<ExternalIdentityEntity>();

    internal DbSet<RoleAssignmentEntity> AccessRoleAssignments =>
        Set<RoleAssignmentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FuaPayDbContext).Assembly);
    }
}
