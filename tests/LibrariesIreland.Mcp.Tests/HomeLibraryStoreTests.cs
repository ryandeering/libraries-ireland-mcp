namespace LibrariesIreland.Mcp.Tests;

[Collection(EnvironmentDependent.Name)]
public class HomeLibraryStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "li-mcp-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

    public HomeLibraryStoreTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _root);
        Environment.SetEnvironmentVariable("LIBRARIES_IE_AUTHORITY", null);
        Environment.SetEnvironmentVariable("LIBRARIES_IE_BRANCH", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _previous);
        Environment.SetEnvironmentVariable("LIBRARIES_IE_AUTHORITY", null);
        Environment.SetEnvironmentVariable("LIBRARIES_IE_BRANCH", null);
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

    private string ConfigPath => Path.Combine(_root, "libraries-ireland-mcp", "config.json");

    [Fact]
    public void AFreshInstallIsNotConfiguredAndDoesNotGuess()
    {
        var store = new HomeLibraryStore();

        Assert.False(store.IsConfigured);
        Assert.Null(store.Current.Authority);
        Assert.Equal(Authorities.FederatedHost, store.Host);
        Assert.Equal("not set", store.Description);
    }

    [Fact]
    public void ASavedChoiceSurvivesARestart()
    {
        new HomeLibraryStore().Save("dublincity", "25772", "Dublin City Ballyfermot");

        var reopened = new HomeLibraryStore();

        Assert.True(reopened.IsConfigured);
        Assert.Equal("dublincity", reopened.Current.Authority);
        Assert.Equal("25772", reopened.Current.BranchIrn);
        Assert.Equal("Dublin City Ballyfermot", reopened.Current.BranchName);
        Assert.Equal("dublincity.spydus.ie", reopened.Host);
        Assert.Contains("Ballyfermot", reopened.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void SavingAServiceWithoutABranchIsAllowed()
    {
        new HomeLibraryStore().Save("corkcoco", null, null);

        var reopened = new HomeLibraryStore();
        Assert.True(reopened.IsConfigured);
        Assert.Null(reopened.Current.BranchName);
        Assert.Equal("Cork County Library", reopened.Description);
    }

    [Fact]
    public void SavingAgainReplacesThePreviousChoice()
    {
        var store = new HomeLibraryStore();
        store.Save("dublincity", "25772", "Dublin City Ballyfermot");
        store.Save("corkcoco", "25685", "Cork County Clonakilty");

        var reopened = new HomeLibraryStore();
        Assert.Equal("corkcoco", reopened.Current.Authority);
        Assert.Equal("Cork County Clonakilty", reopened.Current.BranchName);
    }

    [Fact]
    public void ACorruptConfigFileIsIgnoredRatherThanCrashingTheServer()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, "{ this is not json");

        var store = new HomeLibraryStore();

        // Failing to start would take away every tool; starting unconfigured only takes away the
        // scoped ones, which then ask the user which library they use.
        Assert.False(store.IsConfigured);
    }

    [Fact]
    public void EnvironmentVariablesSeedTheChoiceWhenNoFileExists()
    {
        Environment.SetEnvironmentVariable("LIBRARIES_IE_AUTHORITY", "Cork County");
        Environment.SetEnvironmentVariable("LIBRARIES_IE_BRANCH", "25685");

        var store = new HomeLibraryStore();

        Assert.True(store.IsConfigured);
        Assert.Equal("corkcoco", store.Current.Authority);
        Assert.Equal("25685", store.Current.BranchIrn);
    }

    [Fact]
    public void AnUnrecognisedEnvironmentAuthorityDoesNotConfigureAWrongLibrary()
    {
        Environment.SetEnvironmentVariable("LIBRARIES_IE_AUTHORITY", "Belfast");

        var store = new HomeLibraryStore();

        Assert.False(store.IsConfigured);
        Assert.Equal(Authorities.FederatedHost, store.Host);
    }

    [Fact]
    public void AnAmbiguousEnvironmentAuthorityIsRefusedRatherThanGuessed()
    {
        Environment.SetEnvironmentVariable("LIBRARIES_IE_AUTHORITY", "Cork");

        var store = new HomeLibraryStore();

        Assert.False(store.IsConfigured);
    }

    [Fact]
    public void ASavedFileTakesPrecedenceOverTheEnvironment()
    {
        new HomeLibraryStore().Save("dublincity", "25772", "Dublin City Ballyfermot");
        Environment.SetEnvironmentVariable("LIBRARIES_IE_AUTHORITY", "Cork County");

        var reopened = new HomeLibraryStore();

        Assert.Equal("dublincity", reopened.Current.Authority);
    }

    [Fact]
    public void AnUnwritableConfigDirectoryIsReportedRatherThanCalledASave()
    {
        // Carrying on in memory is fine. Reporting it as saved is not: the setting is gone at the
        // next restart and the user has no way of knowing.
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        Directory.CreateDirectory(ConfigPath);   // a directory where the file should go

        var store = new HomeLibraryStore();
        var outcome = store.Save("dublincity", "25772", "Dublin City Ballyfermot");

        Assert.False(outcome.Persisted);
        Assert.NotNull(outcome.Problem);
        // The reason must not carry the config path: a tool result goes to the model and may be
        // retained, and the absolute path exposes the account name and local directory layout.
        Assert.DoesNotContain(Path.GetTempPath(), outcome.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain("/", outcome.Problem, StringComparison.Ordinal);
        Assert.Equal("dublincity", outcome.Config.Authority);
        Assert.True(store.IsConfigured);

        // The choice really is lost, which is what makes the honest wording necessary.
        Assert.False(new HomeLibraryStore().IsConfigured);
    }

    [Fact]
    public void AWritableDirectoryReportsThatItPersisted()
    {
        var outcome = new HomeLibraryStore().Save("corkcoco", "25685", "Cork County Clonakilty");

        Assert.True(outcome.Persisted);
        Assert.Null(outcome.Problem);
    }
}
