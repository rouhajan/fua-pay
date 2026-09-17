namespace FuaPay.Web.BuildingBlocks.Pdf;

public sealed record PdfAssetsConfiguration(
    string LogoPath,
    string? RegularFontPath,
    string? BoldFontPath);
