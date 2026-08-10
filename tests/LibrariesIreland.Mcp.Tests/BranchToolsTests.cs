namespace LibrariesIreland.Mcp.Tests;

[Collection(EnvironmentDependent.Name)]
public class BranchToolsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "li-mcp-branch-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly List<IDisposable> _disposables = [];

    public BranchToolsTests()
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

    [Fact]
    public async Task SettingAKnownServiceAndBranchStoresBoth()
    {
        var (tools, home, _) = Build();

        var result = await tools.SetHomeLibraryAsync(
            "Dublin City", "Ballyfermot", TestContext.Current.CancellationToken);

        Assert.True(result.IsConfigured);
        Assert.Equal("dublincity", result.Authority);
        Assert.Equal("25772", result.BranchIrn);
        Assert.Equal("dublincity", home.Current.Authority);
        Assert.StartsWith("Saved.", result.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAmbiguousBranchSavesTheServiceAndAsksAboutTheBranch()
    {
        var (tools, home, _) = Build();

        // "Dublin City" prefixes every branch name, so it cannot pick one on its own.
        var result = await tools.SetHomeLibraryAsync(
            "Dublin City", "Dublin City", TestContext.Current.CancellationToken);

        Assert.Equal("dublincity", home.Current.Authority);
        Assert.Null(home.Current.BranchIrn);
        Assert.Contains("Ask the user", result.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownBranchSavesTheServiceAndSaysSo()
    {
        var (tools, home, _) = Build();

        var result = await tools.SetHomeLibraryAsync(
            "Dublin City", "Nowhere", TestContext.Current.CancellationToken);

        Assert.Equal("dublincity", home.Current.Authority);
        Assert.Null(home.Current.BranchName);
        Assert.Contains("no branch matching", result.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAmbiguousServiceIsRefusedAndNothingIsStored()
    {
        var (tools, home, fake) = Build();

        var result = await tools.SetHomeLibraryAsync(
            "Cork", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsConfigured);
        Assert.Null(home.Current.Authority);
        Assert.NotNull(result.KnownAuthorities);
        // Guessing between Cork City and Cork County is worse than asking, so nothing is fetched.
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task AnUnknownServiceIsRefusedAndNothingIsStored()
    {
        var (tools, home, _) = Build();

        var result = await tools.SetHomeLibraryAsync(
            "Belfast", ct: TestContext.Current.CancellationToken);

        Assert.False(result.IsConfigured);
        Assert.Null(home.Current.Authority);
    }

    [Fact]
    public void GetHomeLibraryTellsTheModelToAskWhenNothingIsSet()
    {
        var (tools, _, _) = Build();

        var result = tools.GetHomeLibrary();

        Assert.False(result.IsConfigured);
        Assert.Contains("Do not guess", result.Note, StringComparison.Ordinal);
        Assert.NotNull(result.KnownAuthorities);
    }

    [Fact]
    public async Task AdministrativeLocationsAreNeverOfferedAsBranches()
    {
        var (tools, _, _) = Build();
        await tools.SetHomeLibraryAsync("Dublin City", ct: TestContext.Current.CancellationToken);

        var result = await tools.FindBranchAsync(ct: TestContext.Current.CancellationToken);

        // A store or headquarters entry is not somewhere a borrower can collect a book.
        Assert.DoesNotContain(result.Matches, b => b.Name.Contains("HQ", StringComparison.Ordinal));
        Assert.Equal(4, result.Matches.Count);
    }

    [Fact]
    public async Task ANarrowBranchSearchIncludesContactDetails()
    {
        var (tools, _, _) = Build();
        await tools.SetHomeLibraryAsync("Dublin City", ct: TestContext.Current.CancellationToken);

        var result = await tools.FindBranchAsync(
            "Ballyfermot", ct: TestContext.Current.CancellationToken);

        Assert.Single(result.Matches);
        Assert.Equal("Ballyfermot Road, Dublin 10", result.Matches[0].Address);
        Assert.Equal("012228422", result.Matches[0].Phone);
        Assert.NotNull(result.Matches[0].OpeningHours);
    }

    [Fact]
    public async Task AWideBranchSearchSkipsDetailAndSaysWhy()
    {
        var (tools, _, _) = Build();
        await tools.SetHomeLibraryAsync("Dublin City", ct: TestContext.Current.CancellationToken);

        var result = await tools.FindBranchAsync(ct: TestContext.Current.CancellationToken);

        Assert.All(result.Matches, b => Assert.Null(b.Address));
        Assert.Contains("Narrow the search", result.Note!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBranchListIsFetchedOnceAndThenReused()
    {
        var (tools, _, fake) = Build();
        await tools.SetHomeLibraryAsync("Dublin City", ct: TestContext.Current.CancellationToken);

        await tools.FindBranchAsync(ct: TestContext.Current.CancellationToken);
        await tools.FindBranchAsync(ct: TestContext.Current.CancellationToken);
        await tools.FindBranchAsync(ct: TestContext.Current.CancellationToken);

        // Reference data that changes twice a year should not be re-fetched on every question.
        var listFetches = fake.Requests.Count(r => r.ToString().Contains("COMB", StringComparison.Ordinal));
        Assert.Equal(1, listFetches);
    }

    [Fact]
    public async Task FindBranchWithNoHomeLibrarySearchesEverywhereAndSaysSo()
    {
        var (tools, _, _) = Build();

        var result = await tools.FindBranchAsync(
            "Ballyfermot", ct: TestContext.Current.CancellationToken);

        Assert.Contains("No home library is set", result.Note!, StringComparison.Ordinal);
    }

    private const string BranchDropdown = """
        <select name="BIBLOC" id="BIBLOC">
          <option value="">All locations</option>
          <option value="25772*Dublin City Ballyfermot">Dublin City Ballyfermot</option>
          <option value="25773*Dublin City Ballymun">Dublin City Ballymun</option>
          <option value="25774*Dublin City Cabra">Dublin City Cabra</option>
          <option value="25775*Dublin City Central Library">Dublin City Central Library</option>
          <option value="99001*Dublin City HQ Store">Dublin City HQ Store</option>
        </select>
        """;

    private const string BranchDetailPage = """
        <h1 class="card-title"><span>Dublin City Ballyfermot</span></h1>
        <div><span class="Style1">Address: </span>Ballyfermot Road, Dublin 10</div>
        <div><span class="Style1">Phone: </span>012228422</div>
        <span class="openinghours-day">Monday</span>10:00 AM to 8:00 PM
        """;

    private (BranchTools Tools, HomeLibraryStore Home, FakeCatalogue Fake) Build()
    {
        var fake = new FakeCatalogue()
            .When("GENENQ", BranchDetailPage)
            .When("MSGTRN/WPAC/COMB", BranchDropdown);

        var client = new SpydusClient(fake, TimeSpan.Zero);
        var directory = new BranchDirectory(client);
        var home = new HomeLibraryStore();

        _disposables.Add(client);
        _disposables.Add(directory);
        return (new BranchTools(directory, home), home, fake);
    }
}
