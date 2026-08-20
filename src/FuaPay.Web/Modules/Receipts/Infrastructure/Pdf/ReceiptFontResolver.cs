using PdfSharp.Fonts;

namespace FuaPay.Web.Modules.Receipts.Infrastructure.Pdf;

internal sealed class ReceiptFontResolver : IFontResolver
{
    public const string FamilyName = "FuaPayReceipt";

    private const string RegularFace = "FuaPayReceipt-Regular";
    private const string BoldFace = "FuaPayReceipt-Bold";

    private readonly byte[] _regularFont;
    private readonly byte[] _boldFont;

    public ReceiptFontResolver(
        string regularFontPath,
        string boldFontPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regularFontPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(boldFontPath);

        _regularFont = File.ReadAllBytes(regularFontPath);
        _boldFont = File.ReadAllBytes(boldFontPath);
    }

    public FontResolverInfo? ResolveTypeface(
        string familyName,
        bool isBold,
        bool isItalic)
    {
        if (!string.Equals(
                familyName,
                FamilyName,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new FontResolverInfo(
            isBold ? BoldFace : RegularFace,
            mustSimulateBold: false,
            mustSimulateItalic: isItalic);
    }

    public byte[]? GetFont(string faceName) =>
        faceName switch
        {
            RegularFace => _regularFont,
            BoldFace => _boldFont,
            _ => null
        };
}
