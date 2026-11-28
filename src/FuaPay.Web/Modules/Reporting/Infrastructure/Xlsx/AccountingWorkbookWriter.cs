using System.Globalization;
using System.Text;
using System.Xml;

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

using FuaPay.Web.Modules.Reporting.Application;

namespace FuaPay.Web.Modules.Reporting.Infrastructure.Xlsx;

internal sealed partial class AccountingWorkbookWriter(AccountingReport report, CancellationToken cancellationToken)
{
    private const string ResourcePrefix = "FuaPay.Web.Modules.Reporting.Infrastructure.Xlsx.";

    private readonly List<Hyperlink> _cardLinks = [];
    private readonly List<MergeCell> _summaryMerges = [];
    private readonly List<MergeCell> _controlMerges = [];
    private readonly List<MergeCell> _centreMerges = [];
    private int _rowCount;

    public void Write(Stream output)
    {
        using var workbook = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook);
        var part = workbook.AddWorkbookPart();
        part.Workbook = new Workbook(new BookViews(new WorkbookView { ActiveTab = 0U, FirstSheet = 0U }), new Sheets());
        using (var styles = Resource("AccountingWorkbookStyles.xml"))
        {
            part.AddNewPart<WorkbookStylesPart>().FeedData(styles);
        }
        using (var theme = Resource("AccountingWorkbookTheme.xml"))
        {
            part.AddNewPart<ThemePart>().FeedData(theme);
        }

        Add(part, 1, "Platby ČSOB", [24, 23, 21, 24, 17, 17, 17], 0, 5, Card, _cardLinks);
        Add(part, 2, "Přehled", [23, 13, 13, 14, 13, 16, 20], 0, 4, Summary, extraMerges: _summaryMerges);
        Add(part, 3, "Pohyby kreditu", [23, 22, 33, 22, 18, 20, 51, 40], 3, 5, Credit);
        Add(part, 4, "Zůstatky", [29, 40, 25, 22, 31], 2, 5, Balances);
        Add(part, 5, "Zakázky", [25, 22, 22, 31, 21, 22], 0, 0, Jobs);
        Add(part, 6, "Kontroly", [42, 27, 76, 27], 0, 5, Controls, extraMerges: _controlMerges);
        Add(part, 7, "Střediska", [29, 22, 20, 20, 20, 20, 20, 44, 38], 0, 5, Centres, extraMerges: _centreMerges);
        part.Workbook.Save();
    }

    private void Add(WorkbookPart workbook, uint id, string name, double[] widths, uint paneX,
        uint paneY, Action<OpenXmlWriter> body, List<Hyperlink>? links = null, List<MergeCell>? extraMerges = null)
    {
        var part = workbook.AddNewPart<WorksheetPart>();
        workbook.Workbook!.Sheets!.Append(new Sheet { SheetId = id, Name = name, Id = workbook.GetIdOfPart(part) });
        using var writer = OpenXmlWriter.Create(part);
        writer.WriteStartElement(new Worksheet());
        writer.WriteElement(new SheetProperties(new PageSetupProperties { FitToPage = true }));
        var view = new SheetView { WorkbookViewId = 0, TabSelected = id == 1, ShowGridLines = false };
        if (paneY > 0)
        {
            var active = paneX == 0 ? PaneValues.BottomLeft : PaneValues.BottomRight;
            var first = $"{(char)('A' + paneX)}{paneY + 1}";
            var pane = new Pane
            {
                VerticalSplit = paneY,
                State = PaneStateValues.Frozen,
                ActivePane = active,
                TopLeftCell = first
            };
            if (paneX > 0)
            {
                pane.HorizontalSplit = paneX;
            }

            view.Append(pane, new Selection { Pane = active, ActiveCell = first, SequenceOfReferences = new() { InnerText = first } });
        }
        writer.WriteElement(new SheetViews(view));
        writer.WriteElement(new SheetFormatProperties { BaseColumnWidth = 8, DefaultRowHeight = 15 });
        writer.WriteElement(new Columns(widths.Select((width, index) => new Column
        {
            Min = (uint)index + 1,
            Max = (uint)index + 1,
            Width = width,
            CustomWidth = true
        })));

        writer.WriteStartElement(new SheetData());
        Row(writer, 1, 27, [name], [1]);
        var state = report.Cutoff < report.Period.Until ? " | PRŮBĚŽNÝ STAV" : string.Empty;
        var subtitle = FormattableString.Invariant(
            $"{report.Period.Start:dd.MM.yyyy} – {report.Period.End:dd.MM.yyyy} | řez {AccountingPeriod.Local(report.Cutoff):dd.MM.yyyy HH:mm:ss} ({report.Cutoff:HH:mm:ss} UTC) | Europe/Prague{state}");
        Row(writer, 3, 20, [subtitle], [2]);
        body(writer);
        writer.WriteEndElement();

        var last = (char)('A' + widths.Length - 1);
        var merges = new MergeCells(new MergeCell { Reference = $"A1:{last}2" }, new MergeCell { Reference = $"A3:{last}3" });
        if (extraMerges is not null)
        {
            merges.Append(extraMerges);
        }
        writer.WriteElement(merges);
        if (links is { Count: > 0 })
        {
            writer.WriteElement(new Hyperlinks(links));
        }

        writer.WriteElement(new PageMargins { Left = 0.28, Right = 0.28, Top = 0.42, Bottom = 0.42, Header = 0.12, Footer = 0.18 });
        writer.WriteElement(new PageSetup
        {
            PaperSize = 9,
            Orientation = OrientationValues.Landscape,
            FitToWidth = 1,
            FitToHeight = id is 1 or 2 ? 1U : 0U
        });
        writer.WriteElement(new HeaderFooter(new OddFooter { Text = "&LFUA Pay&R&P / &N" }));
        writer.WriteEndElement();
    }

    private void Row(OpenXmlWriter writer, int index, double height, object?[] values, uint[] styles)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++_rowCount > 40_000)
        {
            throw new AccountingWorkbookLimitException();
        }

        var row = new Row { RowIndex = (uint)index, Height = height, CustomHeight = true };
        for (var i = 0; i < values.Length; ++i)
        {
            var address = $"{(char)('A' + i)}{index}";
            row.Append(Cell(address, styles[i], values[i]));
        }

        writer.WriteElement(row);
    }

    private static Cell Cell(string address, uint style, object? value)
    {
        var cell = new Cell { CellReference = address, StyleIndex = style };
        switch (value)
        {
            case null:
                return cell;
            case Formula formula:
                cell.CellFormula = new CellFormula(formula.Text);
                cell.CellValue = new CellValue(formula.Cached.ToString(CultureInfo.InvariantCulture));
                break;
            case decimal or int or long:
                cell.CellValue = new CellValue(Convert.ToString(value, CultureInfo.InvariantCulture)!);
                break;
            case DateTimeOffset time:
                var local = AccountingPeriod.Local(time);
                if (local.Year < 1900)
                {
                    throw new AccountingWorkbookLimitException();
                }
                var serial = local.ToOADate() - (local < new DateTime(1900, 3, 1) ? 1 : 0);
                cell.CellValue = new CellValue(serial.ToString("G17", CultureInfo.InvariantCulture));
                break;
            default:
                cell.DataType = CellValues.InlineString;
                cell.InlineString = new InlineString(new Text(ExcelText(Convert.ToString(value, CultureInfo.InvariantCulture)!))
                {
                    Space = SpaceProcessingModeValues.Preserve
                });
                break;
        }

        return cell;
    }

    private static string ExcelText(string value)
    {
        if (value.Length > 32_767)
        {
            throw new AccountingWorkbookLimitException();
        }
        var clean = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            clean.Append(rune.IsBmp && !XmlConvert.IsXmlChar((char)rune.Value) ? "\uFFFD" : rune.ToString());
        }

        return clean.ToString();
    }

    private static int LastData(int count) => count == 0 ? 6 : 5 + count;

    private static decimal Money(long amount)
    {
        if (amount is < -999_999_999_999_999L or > 999_999_999_999_999L)
        {
            throw new AccountingWorkbookLimitException();
        }

        return amount / 100m;
    }

    private static Stream Resource(string name) =>
        typeof(AccountingWorkbookWriter).Assembly.GetManifestResourceStream(ResourcePrefix + name)
        ?? throw new InvalidOperationException("Missing accounting workbook layout resource.");

    private sealed record Formula(string Text, decimal Cached);
}
