namespace LibrariesIreland.Mcp.Tests;

// The holdings line is the least forgiving thing the catalogue returns. The segment count varies
// between three and seven, the status itself can contain " - ", as in "Onloan - Due: 26 Aug 2026",
// and a row with no barcode looks much like a row with an extra format segment. Every line below was
// captured from the live feed.
public class HoldingParsingTests
{
    [Fact]
    public void ParsesAvailableCopyWithBarcode()
    {
        var h = RssParser.ParseHolding(
            "Wicklow Headquarters -  (Wicklow County Libraries) - Adult Non-Fiction - 949.7024 "
            + "- Available - 30006000598749");

        Assert.NotNull(h);
        Assert.Equal("Wicklow Headquarters", h.Branch);
        Assert.Equal("Wicklow County Libraries", h.Authority);
        Assert.Equal("Adult Non-Fiction", h.Collection);
        Assert.Equal("949.7024", h.CallNumber);
        Assert.Equal("Available", h.Status);
        Assert.Equal("30006000598749", h.Barcode);
        Assert.True(h.Available);
        Assert.Null(h.DueDate);
    }

    [Fact]
    public void ParsesOnLoanWithDueDateAndExtraFormatSegment()
    {
        // "949.7024 GLE - Store" is a call number that itself contains the separator, and the
        // status is followed by a due-date segment before the barcode.
        var h = RssParser.ParseHolding(
            "Cork City Bishopstown -  (Cork City Libraries) - Adult Non-Fiction "
            + "- 949.7024 GLE - Store - Onloan - Due: 18 Aug 2026 - 30007005064851");

        Assert.NotNull(h);
        Assert.Equal("Cork City Bishopstown", h.Branch);
        Assert.Equal("949.7024 GLE - Store", h.CallNumber);
        Assert.Equal("Onloan", h.Status);
        Assert.Equal("18 Aug 2026", h.DueDate);
        Assert.Equal("30007005064851", h.Barcode);
        Assert.False(h.Available);
    }

    [Fact]
    public void ParsesOnOrderRowThatHasNoBarcode()
    {
        var h = RssParser.ParseHolding(
            "Dublin City Central Library -  (Dublin City Libraries) - Adult Fiction - 813.6 "
            + "- On order");

        Assert.NotNull(h);
        Assert.Equal("On order", h.Status);
        Assert.Null(h.Barcode);
        Assert.False(h.Available);
    }

    [Fact]
    public void ParsesInProcessStatusContainingParentheses()
    {
        var h = RssParser.ParseHolding(
            "Dublin City Ballyfermot -  (Dublin City Libraries) - Adult Fiction - Adult Fiction "
            + "- In-process (Set: 16 Jun 2026) - DCPL7000182405");

        Assert.NotNull(h);
        Assert.Equal("In-process (Set: 16 Jun 2026)", h.Status);
        Assert.Equal("DCPL7000182405", h.Barcode);
        Assert.False(h.Available);
    }

    [Fact]
    public void ParsesReserveShelfStatusNamingAnotherBranch()
    {
        var h = RssParser.ParseHolding(
            "Fingal Donabate Portrane -  (Fingal County Council Libraries) - Adult Fiction - F "
            + "- On reserve shelf at Cork County Clonakilty - FCL30000086502");

        Assert.NotNull(h);
        Assert.Equal("On reserve shelf at Cork County Clonakilty", h.Status);
        Assert.Equal("FCL30000086502", h.Barcode);
        Assert.False(h.Available);
    }

    [Fact]
    public void ParsesInTransitStatus()
    {
        var h = RssParser.ParseHolding(
            "Dublin City Ballymun -  (Dublin City Libraries) - Adult Fiction - F "
            + "- In-transit from Dublin City Ballymun to Wexford Wexford Town (Set: 05 Aug 2026) "
            + "- DCPL7000205456");

        Assert.NotNull(h);
        Assert.StartsWith("In-transit from", h.Status);
        Assert.False(h.Available);
    }

    [Fact]
    public void AnEmptyTrailingSegmentDoesNotBecomeAnEmptyStatus()
    {
        var h = RssParser.ParseHolding("Mayo Castlebar -  (Mayo County Library) - Adult Fiction - ");

        Assert.NotNull(h);
        Assert.False(string.IsNullOrWhiteSpace(h.Status));
        Assert.Equal("Unknown", h.Status);
        Assert.False(h.Available);
    }

    [Fact]
    public void HeaderLinesAreNotMistakenForHoldings()
    {
        Assert.Null(RssParser.ParseHolding("Author: Herbert, Frank"));
        Assert.Null(RssParser.ParseHolding("Published:  7/7/2020"));
        Assert.Null(RssParser.ParseHolding("4 reserves"));
        Assert.Null(RssParser.ParseHolding(""));
    }

    [Fact]
    public void AnUnknownStatusIsNotConfusedWithTheBarcode()
    {
        // The barcode has to be peeled off before the last segment is read as the status.
        // Otherwise the barcode was reported as the status and the real status text vanished,
        // which is the opposite of surfacing an unfamiliar status.
        var h = RssParser.ParseHolding(
            "Mayo Castlebar -  (Mayo County Library) - Adult Fiction - F "
            + "- Brand New Status - MY30001234567");

        Assert.NotNull(h);
        Assert.Equal("Brand New Status", h.Status);
        Assert.Equal("MY30001234567", h.Barcode);
        Assert.Equal("Adult Fiction", h.Collection);
        Assert.Equal("F", h.CallNumber);
        Assert.False(h.Available);
    }

    [Fact]
    public void UnknownStatusIsSurfacedRatherThanDropped()
    {
        var h = RssParser.ParseHolding(
            "Mayo Castlebar -  (Mayo County Library) - Adult Fiction - F - Some Brand New Status");

        Assert.NotNull(h);
        Assert.Equal("Some Brand New Status", h.Status);
        Assert.False(h.Available);
    }

    [Fact]
    public void ParsesAWholeFeedIntoRecordsWithHoldings()
    {
        var xml = File.ReadAllText(Path.Combine("Fixtures", "search-dune.rss"));
        var records = RssParser.Parse(xml);

        Assert.NotEmpty(records);
        Assert.All(records, r => Assert.NotEmpty(r.Brn));
        Assert.All(records, r => Assert.NotEmpty(r.Title));
        Assert.Contains(records, r => r.Holdings.Count > 0);
        Assert.Contains(records, r => r.Author is not null);

        Assert.DoesNotContain(
            records, r => r.Title.Contains("<span", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MalformedFeedReturnsEmptyRatherThanThrowing()
    {
        Assert.Empty(RssParser.Parse("not xml at all <<<"));
        Assert.Empty(RssParser.Parse(""));
    }
}
