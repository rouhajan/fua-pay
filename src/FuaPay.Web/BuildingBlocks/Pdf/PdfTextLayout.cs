using System.Globalization;

namespace FuaPay.Web.BuildingBlocks.Pdf;

internal static class PdfTextLayout
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
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        var words = text.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        var lines = new List<string>();
        var line = string.Empty;

        foreach (var word in words)
        {
            var candidate = line.Length == 0 ? word : $"{line} {word}";

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

            var segments = SplitWordToWidth(word, width, measure);

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
        var starts = StringInfo.ParseCombiningCharacters(word);
        var segments = new List<string>();
        var start = 0;

        while (start < starts.Length)
        {
            var bestEnd = start;

            for (var end = start + 1; end <= starts.Length; end++)
            {
                var firstCharacter = starts[start];
                var lastCharacter = end < starts.Length
                    ? starts[end]
                    : word.Length;

                if (measure(word[firstCharacter..lastCharacter]) > width)
                {
                    break;
                }

                bestEnd = end;
            }

            if (bestEnd == start)
            {
                throw new InvalidOperationException(
                    "Textový prvek PDF je širší než dostupný prostor.");
            }

            var segmentStart = starts[start];
            var segmentEnd = bestEnd < starts.Length
                ? starts[bestEnd]
                : word.Length;
            segments.Add(word[segmentStart..segmentEnd]);
            start = bestEnd;
        }

        return segments;
    }
}
