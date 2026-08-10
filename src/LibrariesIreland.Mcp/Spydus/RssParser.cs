namespace LibrariesIreland.Mcp.Spydus;

internal sealed record RawRecord(
    string Brn,
    string Title,
    string? Author,
    string? Published,
    string? Summary,
    int Reserves,
    // What the feed says the copy count is. Above roughly a dozen copies it stops listing them
    // individually and prints "124 copies" instead, so Holdings can be empty for a title that is
    // widely held. Zero here with no holdings means genuinely nothing.
    int CopiesReported,
    IReadOnlyList<Holding> Holdings);

internal static partial class RssParser
{
    private static readonly XNamespace ContentNs = "http://purl.org/rss/1.0/modules/content/";

    private static readonly string[] StatusPrefixes =
    [
        "Available", "Onloan", "On loan", "On order", "In-process", "In process",
        "On reserve shelf at", "In-transit from", "In transit from", "Lost", "Missing",
        "Withdrawn", "At bindery", "Damaged", "Reference", "Not for loan", "Repair",
        "Being processed", "Overdue", "Claimed",
    ];

    public static List<RawRecord> Parse(string xml)
    {
        var records = new List<RawRecord>();

        var cleaned = SpanTag().Replace(xml, string.Empty);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(cleaned, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return records;
        }

        foreach (var item in doc.Descendants("item"))
        {
            var link = (string?)item.Element("link") ?? string.Empty;
            var brnMatch = BrnInUrl().Match(link);
            if (!brnMatch.Success)
            {
                continue;
            }

            var title = Clean((string?)item.Element("title")) ?? "(untitled)";
            var description = Clean((string?)item.Element("description"));
            var encoded = (string?)item.Element(ContentNs + "encoded") ?? string.Empty;
            var parsed = ParseEncoded(encoded);

            records.Add(new RawRecord(
                brnMatch.Groups[1].Value,
                title,
                parsed.Author,
                parsed.Published,
                description,
                parsed.Reserves,
                parsed.Copies,
                parsed.Holdings));
        }

        return records;
    }

    // One copy per line, in the catalogue's own format:
    //   Branch -  (Authority) - Collection - Call number[ - Format] - Status[ - Due: date] - Barcode
    //
    // The segment count varies between three and seven, and the status itself can contain the
    // separator, as in "Onloan - Due: 26 Aug 2026", so this anchors on the branch and authority
    // prefix and then locates the status by keyword rather than splitting on " - ".
    internal static Holding? ParseHolding(string line)
    {
        var match = HoldingLine().Match(line);
        if (!match.Success)
        {
            return null;
        }

        var branch = match.Groups["branch"].Value.Trim();
        var authority = match.Groups["auth"].Value.Trim();
        var parts = match.Groups["rest"].Value.Split(" - ", StringSplitOptions.TrimEntries);

        var statusIdx = -1;
        for (var i = 0; i < parts.Length; i++)
        {
            if (IsStatus(parts[i]))
            {
                statusIdx = i;
                break;
            }
        }

        string status;
        string? due = null;
        string? barcode = null;
        string? collection = null;
        string? callNumber = null;

        if (statusIdx < 0)
        {
            var tail = parts.Length;
            if (tail > 1 && Barcode().IsMatch(parts[tail - 1]))
            {
                barcode = parts[tail - 1];
                tail--;
            }

            var candidate = parts[tail - 1];
            status = string.IsNullOrWhiteSpace(candidate) ? "Unknown" : candidate;

            if (tail > 1)
            {
                collection = parts[0];
            }

            if (tail > 2)
            {
                callNumber = string.Join(" - ", parts[1..(tail - 1)]);
            }
        }
        else
        {
            status = parts[statusIdx];

            if (statusIdx > 0)
            {
                collection = parts[0];
            }

            if (statusIdx > 1)
            {
                callNumber = string.Join(" - ", parts[1..statusIdx]);
            }

            var next = statusIdx + 1;
            if (next < parts.Length)
            {
                var dueMatch = DueSegment().Match(parts[next]);
                if (dueMatch.Success)
                {
                    due = dueMatch.Groups["d"].Value.Trim();
                    next++;
                }
            }

            if (next < parts.Length && Barcode().IsMatch(parts[next]))
            {
                barcode = parts[next];
            }
        }

        if (barcode is null
            && parts.Length > 0
            && statusIdx != parts.Length - 1
            && Barcode().IsMatch(parts[^1]))
        {
            barcode = parts[^1];
        }

        var available = status.StartsWith("Available", StringComparison.OrdinalIgnoreCase);
        return new Holding(branch, authority, collection, callNumber, status, due, barcode, available);
    }

    private static (string? Author, string? Published, int Reserves, int Copies, List<Holding> Holdings)
        ParseEncoded(string encoded)
    {
        string? author = null;
        string? published = null;
        var reserves = 0;
        var copies = 0;
        var holdings = new List<Holding>();

        foreach (var chunk in BrTag().Split(encoded))
        {
            var line = Clean(chunk);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (TryStrip(line, "Author:", out var authorValue))
            {
                author ??= authorValue;
                continue;
            }

            if (TryStrip(line, "Published:", out var publishedValue))
            {
                published ??= publishedValue;
                continue;
            }

            var reservesMatch = ReservesLine().Match(line);
            if (reservesMatch.Success)
            {
                if (int.TryParse(reservesMatch.Groups["n"].Value, out var parsedReserves))
                {
                    reserves = parsedReserves;
                }

                continue;
            }

            var copiesMatch = CopiesLine().Match(line);
            if (copiesMatch.Success)
            {
                if (int.TryParse(copiesMatch.Groups["n"].Value, out var parsedCopies))
                {
                    copies = parsedCopies;
                }

                continue;
            }

            var holding = ParseHolding(line);
            if (holding is not null)
            {
                holdings.Add(holding);
            }
        }

        return (author, published, reserves, copies, holdings);
    }

    private static bool IsStatus(string segment)
    {
        foreach (var prefix in StatusPrefixes)
        {
            if (segment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryStrip(string line, string prefix, out string value)
    {
        if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = line[prefix.Length..].Trim();
            return value.Length > 0;
        }

        value = string.Empty;
        return false;
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var stripped = WebUtility.HtmlDecode(AnyTag().Replace(text, string.Empty));
        stripped = stripped.Replace('\u00A0', ' ').Trim();   // nbsp is common in the markup

        return stripped.Length == 0 ? null : stripped;
    }

    [GeneratedRegex(@"^(?<branch>.+?)\s+-\s+\((?<auth>[^)]+)\)\s+-\s+(?<rest>.+)$")]
    private static partial Regex HoldingLine();

    [GeneratedRegex(@"</?span[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex SpanTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrTag();

    [GeneratedRegex(@"BRN=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex BrnInUrl();

    [GeneratedRegex(@"^(?<n>\d+)\s+reserves?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReservesLine();

    [GeneratedRegex(@"^(?<n>\d+)\s+cop(?:y|ies)$", RegexOptions.IgnoreCase)]
    private static partial Regex CopiesLine();

    [GeneratedRegex(@"^[A-Z]{0,6}\d[A-Z0-9]{5,}$")]
    private static partial Regex Barcode();

    [GeneratedRegex(@"^Due:\s*(?<d>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DueSegment();
}
