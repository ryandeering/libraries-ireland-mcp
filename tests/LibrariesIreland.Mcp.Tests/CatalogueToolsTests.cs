namespace LibrariesIreland.Mcp.Tests;

[Collection(EnvironmentDependent.Name)]
public class CatalogueToolsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "li-mcp-tools-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly List<IDisposable> _disposables = [];

    public CatalogueToolsTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _root);
        Environment.SetEnvironmentVariable("LIBRARIES_IE_AUTHORITY", null);
        Environment.SetEnvironmentVariable("LIBRARIES_IE_BRANCH", null);
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
        {
            d.Dispose();
        }

        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _previousConfig);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    private const string WicklowCopy =
        "Wicklow Headquarters -  (Wicklow County Libraries) - Adult Non-Fiction - 949.7 "
        + "- Available - 30006000598749";

    private const string CorkCopy =
        "Cork City Bishopstown -  (Cork City Libraries) - Adult Non-Fiction - 949.7 "
        + "- Onloan - Due: 18 Aug 2026 - 30007005064851";

    [Fact]
    public async Task ACopyInTheUsersOwnServiceIsReportedAsTheirs()
    {
        // The feed writes "Wicklow County Libraries" where the configured name is "Wicklow County
        // Library". Comparing those as text said the user's library did not hold the book.
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("Some History", "2026262", WicklowCopy, CorkCopy));

        var report = await Build(fake, "wicklow").WhereCanIGetThisAsync(
            "2026262", TestContext.Current.CancellationToken);

        Assert.True(report.HeldByHomeLibrary);
        Assert.True(report.OnShelfInHomeLibrary);
        Assert.Single(report.HomeLibraryCopies);
        Assert.Equal("Wicklow Headquarters", report.HomeLibraryCopies[0].Branch);
        Assert.Contains("On the shelf now", report.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACopyInAnotherCountyIsReportedAsRequestable()
    {
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("Some History", "2026262", CorkCopy));

        var report = await Build(fake, "dublincity").WhereCanIGetThisAsync(
            "2026262", TestContext.Current.CancellationToken);

        Assert.False(report.HeldByHomeLibrary);
        Assert.Empty(report.HomeLibraryCopies);
        Assert.Contains("Cork City Libraries", report.OtherAuthorities);
        Assert.Contains("sent to your local branch", report.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATitleHeldNowhereSaysSoPlainly()
    {
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("Obscure Thing", "111"));

        var report = await Build(fake, "dublincity").WhereCanIGetThisAsync(
            "111", TestContext.Current.CancellationToken);

        Assert.False(report.HeldByHomeLibrary);
        Assert.Empty(report.OtherCopies);
        Assert.Contains("No copies", report.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWidelyHeldTitleIsNotReportedAsHavingNoCopies()
    {
        // Above roughly a dozen copies the catalogue prints "124 copies" instead of listing them.
        // Reading that as zero told users the most popular books in the country were unavailable.
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.SummarisedFeed("A Popular Book", "3702982", 124, 6));

        var report = await Build(fake, "dublincity").WhereCanIGetThisAsync(
            "3702982", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("No copies", report.Verdict, StringComparison.Ordinal);
        Assert.Contains("124 copies", report.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASummarisedCopyCountIsCarriedIntoSearchResults()
    {
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.SummarisedFeed("A Popular Book", "1", 124, 6))
            .When("BIBENQ", FakeCatalogue.ResultsPage(1));

        var response = await Build(fake, "dublincity").BrowseSubjectAsync(
            "Ireland", ct: TestContext.Current.CancellationToken);

        var result = Assert.Single(response.Results);
        Assert.Equal(124, result.TotalCopies);
        Assert.DoesNotContain("No copies", result.AvailabilityNote, StringComparison.Ordinal);
        Assert.Contains("shelf status is unknown", result.AvailabilityNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATitleWithNeitherCopiesNorACountStillReadsAsUnheld()
    {
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("Obscure Thing", "222"));

        var report = await Build(fake, "dublincity").WhereCanIGetThisAsync(
            "222", TestContext.Current.CancellationToken);

        Assert.Contains("No copies", report.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingRecordIsNotPresentedAsABook()
    {
        var fake = new FakeCatalogue().When("FMT=RSS", FakeCatalogue.EmptyFeed());

        var report = await Build(fake, "dublincity").WhereCanIGetThisAsync(
            "999999", TestContext.Current.CancellationToken);

        Assert.Contains("No record", report.Verdict, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AScopedSearchWithNoHomeLibrarySetAsksRatherThanGuessing()
    {
        var fake = new FakeCatalogue().When("FMT=RSS", FakeCatalogue.EmptyFeed());

        var response = await Build(fake, authority: null)
            .BrowseSubjectAsync("Yugoslavia", ct: TestContext.Current.CancellationToken);

        Assert.Empty(response.Results);
        Assert.Contains("Ask the user", response.Note!, StringComparison.Ordinal);
        // Nothing should have been requested: guessing a library is worse than not answering.
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task AHomeScopedSearchGoesToThatServicesOwnSubdomain()
    {
        // The federated host filters which titles match but not which copies are listed, so a
        // local-availability question asked there would report another county's copy as local.
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("Beekeeping", "642276", WicklowCopy))
            .When("BIBENQ", FakeCatalogue.ResultsPage(1));

        await Build(fake, "wicklow").BrowseSubjectAsync("beekeeping", ct: TestContext.Current.CancellationToken);

        Assert.All(fake.Requests, r => Assert.Equal("wicklow.spydus.ie", r.Host));
    }

    [Fact]
    public async Task ANationwideSearchGoesToTheFederatedHost()
    {
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("Beekeeping", "642276", CorkCopy))
            .When("BIBENQ", FakeCatalogue.ResultsPage(1));

        await Build(fake, "wicklow").BrowseSubjectAsync(
            "beekeeping", Scope.Nationwide, ct: TestContext.Current.CancellationToken);

        Assert.All(fake.Requests, r => Assert.Equal("librariesireland.spydus.ie", r.Host));
    }

    [Fact]
    public async Task AvailableNowNarrowsToTheUsersOwnBranch()
    {
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("Beekeeping", "642276", WicklowCopy))
            .When("BIBENQ", FakeCatalogue.ResultsPage(1));

        await Build(fake, "wicklow", "Wicklow Headquarters")
            .BrowseSubjectAsync("beekeeping", availableNow: true, ct: TestContext.Current.CancellationToken);

        Assert.Contains("ITMLOC: 25772", fake.LastQuery!, StringComparison.Ordinal);
        Assert.Contains("ITMRCVF: 1", fake.LastQuery!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoSortIsEverSent()
    {
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("A History", "1", WicklowCopy))
            .When("BIBENQ", FakeCatalogue.ResultsPage(1));

        await Build(fake, "wicklow").BrowseSubjectAsync(
            "Yugoslavia", ct: TestContext.Current.CancellationToken);

        Assert.All(fake.Requests, r => Assert.DoesNotContain("SORTS", r.Query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task BrowseSubjectDefaultsToNonFiction()
    {
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("A History", "1", WicklowCopy))
            .When("BIBENQ", FakeCatalogue.ResultsPage(1));

        await Build(fake, "wicklow").BrowseSubjectAsync("Yugoslavia", ct: TestContext.Current.CancellationToken);

        Assert.Contains("SU: YUGOSLAVIA", fake.LastQuery!, StringComparison.Ordinal);
        Assert.Contains("DYN: FBK", fake.LastQuery!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvalidBrnIsRefusedBeforeAnyRequestIsMade()
    {
        var fake = new FakeCatalogue();

        var report = await Build(fake, "dublincity").WhereCanIGetThisAsync(
            "12x34", TestContext.Current.CancellationToken);

        Assert.Contains("not a valid BRN", report.Verdict, StringComparison.Ordinal);
        // "12x34" would otherwise have been reshaped into 1234, a real but different record.
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task AnInvalidIsbnIsRefusedBeforeAnyRequestIsMade()
    {
        var fake = new FakeCatalogue();

        var response = await Build(fake, "dublincity").SearchCatalogueAsync(
            isbn: "not-an-isbn", ct: TestContext.Current.CancellationToken);

        Assert.Contains("not a valid ISBN", response.Note!, StringComparison.Ordinal);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task AHyphenatedIsbnBecomesASingleQueryToken()
    {
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("Dune", "3496463", WicklowCopy))
            .When("BIBENQ", FakeCatalogue.ResultsPage(1));

        await Build(fake, "dublincity")
            .SearchCatalogueAsync(
                isbn: "978-0-575-08150-5", scope: Scope.Nationwide, ct: TestContext.Current.CancellationToken);

        Assert.Contains("ISBN: 9780575081505", fake.LastQuery!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnrecognisedLanguageIsRefusedBeforeAnyRequestIsMade()
    {
        var fake = new FakeCatalogue();

        var response = await Build(fake, "dublincity").SearchCatalogueAsync(
            query: "dune", language: "Klingon", ct: TestContext.Current.CancellationToken);

        Assert.Contains("not a recognised language", response.Note!, StringComparison.Ordinal);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task ASearchWithNoCriteriaAsksForSomeRatherThanReturningEverything()
    {
        var fake = new FakeCatalogue();

        var response = await Build(fake, "dublincity").SearchCatalogueAsync(ct: TestContext.Current.CancellationToken);

        Assert.Empty(response.Results);
        Assert.Contains("at least one of", response.Note!, StringComparison.Ordinal);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task TheTotalCountComesFromTheHtmlPageBecauseRssDoesNotCarryOne()
    {
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("A History", "1", WicklowCopy))
            .When("BIBENQ", FakeCatalogue.ResultsPage(173));

        var response = await Build(fake, "wicklow").BrowseSubjectAsync(
            "Yugoslavia", ct: TestContext.Current.CancellationToken);

        Assert.Equal(173, response.TotalMatches);
        Assert.Single(response.Results);
        Assert.Contains("Showing 1 of 173", response.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASlowIsbnLookupDoesNotDiscardTheBookDetail()
    {
        // An HttpClient timeout throws TaskCanceledException, not HttpRequestException. Catching
        // only the latter meant a slow optional request threw away a result already in hand.
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("Some History", "2026262", WicklowCopy))
            .Fails("BRN=2026262", new TaskCanceledException("timeout", new TimeoutException()));

        var book = await Build(fake, "wicklow").GetBookAsync(
            "2026262", ct: TestContext.Current.CancellationToken);

        Assert.Equal("Some History", book.Title);
        Assert.Single(book.Holdings);
        Assert.Null(book.Isbn);
    }

    [Fact]
    public async Task AFailedIsbnLookupDoesNotDiscardTheBookDetail()
    {
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("Some History", "2026262", WicklowCopy))
            .Fails("BRN=2026262", new HttpRequestException("boom"));

        var book = await Build(fake, "wicklow").GetBookAsync(
            "2026262", ct: TestContext.Current.CancellationToken);

        Assert.Equal("Some History", book.Title);
        Assert.Null(book.Isbn);
    }

    [Fact]
    public async Task ASlowCountLookupDoesNotDiscardTheSearchResults()
    {
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("A History", "1", WicklowCopy))
            .Fails("BIBENQ", new TaskCanceledException("timeout", new TimeoutException()));

        var response = await Build(fake, "wicklow").BrowseSubjectAsync(
            "Yugoslavia", ct: TestContext.Current.CancellationToken);

        Assert.Single(response.Results);
        Assert.Null(response.TotalMatches);
    }

    [Fact]
    public async Task GetBookReportsEveryCopyAndLinksToTheRecord()
    {
        var fake = new FakeCatalogue()
            .When("FMT=RSS", FakeCatalogue.Feed("Some History", "2026262", WicklowCopy, CorkCopy))
            .When("BRN=2026262", "<div>no isbn caption here</div>");

        var book = await Build(fake, "dublincity").GetBookAsync("2026262", ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, book.TotalCopies);
        Assert.Equal(1, book.AvailableCopies);
        Assert.Contains("BRN=2026262", book.CatalogueUrl, StringComparison.Ordinal);
        // No ISBN on the page means no ISBN reported, rather than a stray number from the markup.
        Assert.Null(book.Isbn);
        Assert.Null(book.CoverImageUrl);
    }

    private CatalogueTools Build(FakeCatalogue fake, string? authority, string? branch = null)
    {
        var client = new SpydusClient(fake, TimeSpan.Zero);
        var home = new HomeLibraryStore();
        if (authority is not null)
        {
            home.Save(authority, branch is null ? null : "25772", branch);
        }

        _disposables.Add(client);
        return new CatalogueTools(client, home);
    }
}
