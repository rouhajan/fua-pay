using System.Globalization;

namespace FuaPay.Web.Modules.Receipts.Infrastructure.Pdf;

internal static class ReceiptTextLayout
{
    public static IReadOnlyList<string> Wrap(
        string text,
        double width,
        Func<string, double> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Šířka textového sloupce musí být kladná.");
        }

        var words = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        var lines = new List<string>();
        var line = string.Empty;

        foreach (var word in words)
        {
            var candidate = line.Length == 0
                ? word
                : $"{line} {word}";

            if (measure(candidate) <= width)
            {
                line = candidate;
                continue;
            }

            if (line.Length > 0)
            {
                lines.Add(line);
                line = string.Empty;
            }

            var segments = SplitWordToWidth(
                word,
                width,
                measure);

            for (var index = 0; index < segments.Count - 1; index++)
            {
                lines.Add(segments[index]);
            }

            line = segments[^1];
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        return lines;
    }

    public static bool FitsBeforeFooter(
        double contentY,
        double contentHeight,
        double contentGap,
        double footerY) =>
        contentY + contentHeight + contentGap <= footerY;

    private static IReadOnlyList<string> SplitWordToWidth(
        string word,
        double width,
        Func<string, double> measure)
    {
        var textElementStarts =
            StringInfo.ParseCombiningCharacters(word);
        var segments = new List<string>();
        var startElementIndex = 0;

        while (startElementIndex < textElementStarts.Length)
        {
            var bestEndElementIndex = startElementIndex;

            for (
                var endElementIndex = startElementIndex + 1;
                endElementIndex <= textElementStarts.Length;
                endElementIndex++)
            {
                var startCharacterIndex =
                    textElementStarts[startElementIndex];
                var endCharacterIndex =
                    endElementIndex < textElementStarts.Length
                        ? textElementStarts[endElementIndex]
                        : word.Length;
                var candidate =
                    word[startCharacterIndex..endCharacterIndex];

                if (measure(candidate) > width)
                {
                    break;
                }

                bestEndElementIndex = endElementIndex;
            }

            if (bestEndElementIndex == startElementIndex)
            {
                throw new InvalidOperationException(
                    "Textový prvek PDF je širší než dostupný prostor.");
            }

            var segmentStartCharacterIndex =
                textElementStarts[startElementIndex];
            var segmentEndCharacterIndex =
                bestEndElementIndex < textElementStarts.Length
                    ? textElementStarts[bestEndElementIndex]
                    : word.Length;

            segments.Add(
                word[segmentStartCharacterIndex..segmentEndCharacterIndex]);

            startElementIndex = bestEndElementIndex;
        }

        return segments;
    }
}
