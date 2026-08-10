namespace LibrariesIreland.Mcp.Tests;

public class HtmlScrapeTests
{
    private const string IsbnMarkup =
        """<div class="row"><div class="col-sm-3 col-md-3 fd-caption"><span>ISBN: </span></div>""" +
        """<div class="col pl-sm-0"><span class="d-block">9781805262862 (hbk)</span></div></div>""";

    [Fact]
    public void ReadsIsbnFromTheRecordPageCaption()
        => Assert.Equal("9781805262862", HtmlScrape.FirstIsbn(IsbnMarkup));

    [Fact]
    public void QualifiersAfterTheIsbnAreDropped()
        => Assert.Equal("9781805262862", HtmlScrape.FirstIsbn(IsbnMarkup.Replace("(hbk)", "(pbk. : alk. paper)")));

    [Fact]
    public void TenDigitIsbnEndingInXIsAccepted()
        => Assert.Equal("080442957X", HtmlScrape.FirstIsbn(IsbnMarkup.Replace("9781805262862", "080442957X")));

    [Fact]
    public void APageWithNoIsbnYieldsNullRatherThanAStrayNumber()
    {
        // Regression: the page is littered with 10-digit set and record ids. Returning one of
        // those as an ISBN would send the user looking for a book that doesn't exist.
        var page = """
            <main data-settotal="16"><a href="/cgi-bin/spydus.exe/XFACETS/WPAC/BIBENQ/1142092086">facets</a>
            <a href="/cgi-bin/spydus.exe/SET/WPAC/BIBENQ/1142092086/173400562">rec</a>
            <div class="fd-caption"><span>Dewey class: </span></div><div class="col"><span>949.742</span></div></main>
            """;

        Assert.Null(HtmlScrape.FirstIsbn(page));
    }

    [Fact]
    public void ReadsTheTotalMatchCount()
    {
        Assert.Equal(526, HtmlScrape.TotalMatches("""<main data-displaytype="LIST" data-settotal="526">"""));
        Assert.Null(HtmlScrape.TotalMatches("<h1>Error result</h1>"));
    }

    [Fact]
    public void BranchListParsesIrnAndNameAndDropsAdminLocations()
    {
        var html = """
            <select name="BIBLOC" id="BIBLOC">
              <option value="">All locations</option>
              <option value="25772*Dublin City Ballyfermot">Dublin City Ballyfermot</option>
              <option value="25620*Carlow Branch">Carlow Branch</option>
              <option value="99001*Cork City New Branch (DO NOT USE)">Cork City New Branch (DO NOT USE)</option>
              <option value="99002*Carlow Default Cleanup">Carlow Default Cleanup</option>
              <option value="99003*Dublin City Store">Dublin City Store</option>
            </select>
            """;

        var branches = HtmlScrape.BranchList(html);

        Assert.Equal(2, branches.Count);
        Assert.Equal("25772", branches[0].Irn);
        Assert.Equal("Dublin City Ballyfermot", branches[0].Name);
        Assert.DoesNotContain(branches, b => b.Name.Contains("DO NOT USE"));
        Assert.DoesNotContain(branches, b => b.Name.Contains("Cleanup"));
        Assert.DoesNotContain(branches, b => b.Name.Contains("Store"));
    }

    [Fact]
    public void BranchListOfAPageWithoutTheDropdownIsEmpty()
        => Assert.Empty(HtmlScrape.BranchList("<html><body>no form here</body></html>"));
}
