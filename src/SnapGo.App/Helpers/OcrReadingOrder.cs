namespace SnapGo.App.Helpers;

/// <summary>One recognized line and its axis-aligned box, in image pixels.</summary>
public readonly record struct OcrLine(string Text, double Left, double Top, double Right, double Bottom)
{
    public double Height => Bottom - Top;
}

/// <summary>
/// Puts recognized lines into reading order. A line joins a row only when the
/// vertical overlap is at least half the taller box, so a tall column does not
/// absorb a short line that merely crosses its lower half.
/// </summary>
public static class OcrReadingOrder
{
    public static string Arrange(IReadOnlyList<OcrLine> lines)
    {
        if (lines.Count == 0)
            return string.Empty;

        var pending = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Text) && line.Height > 0 && line.Right > line.Left)
            .OrderBy(line => line.Top)
            .ThenBy(line => line.Left)
            .ToList();

        var rows = new List<Row>();
        foreach (var line in pending)
        {
            Row? best = null;
            var bestOverlap = 0d;
            foreach (var row in rows)
            {
                var overlap = Overlap(line.Top, line.Bottom, row.Top, row.Bottom);
                if (overlap <= 0)
                    continue;
                var taller = Math.Max(line.Height, row.Bottom - row.Top);
                if (overlap < taller * 0.5)
                    continue;
                if (overlap > bestOverlap)
                {
                    best = row;
                    bestOverlap = overlap;
                }
            }

            if (best is null)
                rows.Add(new Row(line));
            else
                best.Add(line);
        }

        return string.Join("\n", rows
            .OrderBy(row => row.Top)
            .ThenBy(row => row.Lines.Min(line => line.Left))
            .Select(row => string.Join(" ", row.Lines.OrderBy(line => line.Left).Select(line => line.Text.Trim()))));
    }

    private static double Overlap(double topA, double bottomA, double topB, double bottomB)
        => Math.Min(bottomA, bottomB) - Math.Max(topA, topB);

    private sealed class Row
    {
        public Row(OcrLine first)
        {
            Top = first.Top;
            Bottom = first.Bottom;
            Lines.Add(first);
        }

        public double Top { get; private set; }
        public double Bottom { get; private set; }
        public List<OcrLine> Lines { get; } = new();

        public void Add(OcrLine line)
        {
            Lines.Add(line);
            Top = Math.Min(Top, line.Top);
            Bottom = Math.Max(Bottom, line.Bottom);
        }
    }
}
