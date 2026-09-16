using FuaPay.Web.Modules.FinancialDocuments.Domain;

namespace FuaPay.Web.Modules.FinancialDocuments.Application;

public sealed class FinancialDocumentSourceAlreadyExistsException :
    Exception
{
    public FinancialDocumentSourceAlreadyExistsException(
        FinancialDocumentSourceType sourceType,
        Guid sourceId,
        Exception innerException)
        : base(
            $"Financial document source '{sourceType}:{sourceId}' already exists.",
            innerException)
    {
        SourceType = sourceType;
        SourceId = sourceId;
    }

    public FinancialDocumentSourceType SourceType { get; }

    public Guid SourceId { get; }
}
