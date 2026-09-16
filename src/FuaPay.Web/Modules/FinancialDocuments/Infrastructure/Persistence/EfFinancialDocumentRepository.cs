using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Modules.FinancialDocuments.Application;
using FuaPay.Web.Modules.FinancialDocuments.Domain;

using Microsoft.EntityFrameworkCore;

namespace FuaPay.Web.Modules.FinancialDocuments.Infrastructure.Persistence;

internal sealed class EfFinancialDocumentRepository :
    IFinancialDocumentRepository
{
    private readonly FuaPayDbContext _dbContext;

    public EfFinancialDocumentRepository(FuaPayDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<FinancialDocument?> FindBySourceAsync(
        FinancialDocumentSourceType sourceType,
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        ValidateSource(sourceType, sourceId);

        var entity = await _dbContext.FinancialDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.SourceType == (int)sourceType &&
                    item.SourceId == sourceId,
                cancellationToken);

        return entity is null ? null : Restore(entity);
    }

    public void Stage(FinancialDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _dbContext.FinancialDocuments.Add(
            new FinancialDocumentEntity
            {
                DocumentId = document.DocumentId,
                DocumentNumber = document.DocumentNumber,
                DocumentType = (int)document.DocumentType,
                SourceType = (int)document.SourceType,
                SourceId = document.SourceId,
                CustomerUserId = document.Customer.CustomerUserId,
                CustomerDisplayName = document.Customer.DisplayName,
                CustomerEmail = document.Customer.Email,
                AmountMinorUnits = document.AmountMinorUnits,
                Currency = document.Currency,
                FinancialEventAt = document.FinancialEventAt,
                IssuedAt = document.IssuedAt,
                SettlementMethod = (int)document.SettlementMethod,
                IssuerLegalName = document.Issuer?.LegalName,
                IssuerUnitName = document.Issuer?.UnitName,
                IssuerAddressLine1 = document.Issuer?.AddressLine1,
                IssuerAddressLine2 = document.Issuer?.AddressLine2,
                IssuerCountry = document.Issuer?.Country,
                IssuerRegistrationNumber =
                    document.Issuer?.RegistrationNumber,
                IssuerVatNumber = document.Issuer?.VatNumber,
                IssuerContactEmail = document.Issuer?.ContactEmail,
                Provider = document.Provider?.Provider,
                ProviderReference = document.Provider?.Reference,
                ProviderOrderNumber = document.Provider?.OrderNumber,
                JobId = document.Job?.JobId,
                JobNumber = document.Job?.JobNumber,
                JobTitle = document.Job?.Title,
                JobDescription = document.Job?.Description,
                ServiceUnitName = document.Job?.ServiceUnitName,
                SchemaVersion = document.SchemaVersion,
                RenderVersion = document.RenderVersion
            });
    }

    private static FinancialDocument Restore(
        FinancialDocumentEntity entity)
    {
        var provider = entity.Provider is null
            ? null
            : new FinancialDocumentProviderSnapshot(
                entity.Provider,
                entity.ProviderReference,
                entity.ProviderOrderNumber);

        var job = entity.JobId.HasValue
            ? new FinancialDocumentJobSnapshot(
                entity.JobId.Value,
                entity.JobNumber!,
                entity.JobTitle!,
                entity.JobDescription!,
                entity.ServiceUnitName!)
            : null;

        var issuer = entity.IssuerLegalName is null
            ? null
            : new FinancialDocumentIssuerSnapshot(
                entity.IssuerLegalName,
                entity.IssuerUnitName!,
                entity.IssuerAddressLine1!,
                entity.IssuerAddressLine2!,
                entity.IssuerCountry!,
                entity.IssuerRegistrationNumber!,
                entity.IssuerVatNumber!,
                entity.IssuerContactEmail!);

        return new FinancialDocument(
            entity.DocumentId,
            entity.DocumentNumber,
            (FinancialDocumentType)entity.DocumentType,
            (FinancialDocumentSourceType)entity.SourceType,
            entity.SourceId,
            new FinancialDocumentCustomerSnapshot(
                entity.CustomerUserId,
                entity.CustomerDisplayName,
                entity.CustomerEmail),
            entity.AmountMinorUnits,
            entity.Currency,
            entity.FinancialEventAt,
            entity.IssuedAt,
            (FinancialDocumentSettlementMethod)entity.SettlementMethod,
            issuer,
            provider,
            job,
            entity.SchemaVersion,
            entity.RenderVersion);
    }

    private static void ValidateSource(
        FinancialDocumentSourceType sourceType,
        Guid sourceId)
    {
        if (
            sourceType == FinancialDocumentSourceType.Unknown ||
            !Enum.IsDefined(sourceType))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceType));
        }

        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException(
                "ID zdroje nesmí být prázdné.",
                nameof(sourceId));
        }
    }
}
