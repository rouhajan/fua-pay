using FuaPay.Web.BuildingBlocks.Domain;

namespace FuaPay.Web.Modules.Credits.Application;

public interface ICreditAvailabilityRepository
{
    Task<Money> GetTotalBlockingAmountAsync(
        Guid creditAccountId,
        CancellationToken cancellationToken = default);
}
