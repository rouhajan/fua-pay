using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.FinancialDocuments.Infrastructure.Persistence;

internal sealed class EfFinancialDocumentQueries : IFinancialDocumentQueries
{
    private readonly FuaPayDbContext _dbContext;

    public EfFinancialDocumentQueries(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<FinancialDocument?> FindByIdForCustomerAsync(
        Guid documentId,
        Guid customerUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(documentId, nameof(documentId));
        ValidateId(customerUserId, nameof(customerUserId));

        var entity = await _dbContext.FinancialDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.DocumentId == documentId &&
                    item.CustomerUserId == customerUserId,
                cancellationToken);

        return entity is null
            ? null
            : EfFinancialDocumentRepository.Restore(entity);
    }

    public async Task<FinancialDocument?> FindByIdForAdminAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(documentId, nameof(documentId));

        var entity = await _dbContext.FinancialDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.DocumentId == documentId,
                cancellationToken);

        return entity is null
            ? null
            : EfFinancialDocumentRepository.Restore(entity);
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "ID nesmí být prázdné.",
                parameterName);
        }
    }
}
