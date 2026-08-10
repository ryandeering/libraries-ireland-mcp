namespace LibrariesIreland.Mcp.Tests;

// where_can_i_get_this queries the federated host, so copies from every county come back mixed
// together. Misclassifying one produces a confident false statement: "not held by your library"
// about a book on the shelf. These use the authority names exactly as the live feed writes them.
public class HoldingClassificationTests
{

    [Theory]
    [MemberData(nameof(Fixtures.LiveAuthorityNames), MemberType = typeof(Fixtures))]
    public void ACopyIsRecognisedAsBelongingToItsOwnService(string feedName)
    {
        // Regression: comparing the feed's display name against the configured one as text misfiled
        // 24 of the 27 services.
        var key = Authorities.Resolve(feedName);
        Assert.NotNull(key);
        Assert.True(
            CatalogueTools.BelongsToAuthority(Copy(feedName), key),
            $"a copy held by '{feedName}' was not recognised as belonging to '{key}'");
    }

    [Theory]
    [InlineData("Fingal County Council Libraries", "fingal")]
    [InlineData("Clare County Council Libraries", "clare")]
    [InlineData("Wicklow County Libraries", "wicklow")]
    [InlineData("Waterford Libraries", "waterford")]
    [InlineData("South Dublin County Council Libraries", "southdublin")]
    public void ServicesWhoseFeedSpellingDiffersAreStillMatched(string feedName, string key)
    {
        var belongs = CatalogueTools.BelongsToAuthority(Copy(feedName), key);
        Assert.True(belongs, $"'{feedName}' was not matched to '{key}'");
    }

    [Fact]
    public void ACopyFromAnotherCountyIsNotClaimedAsLocal()
    {
        Assert.False(CatalogueTools.BelongsToAuthority(Copy("Cork County Council Libraries"), "dublincity"));
        Assert.False(CatalogueTools.BelongsToAuthority(Copy("Cork City Libraries"), "corkcoco"));
        Assert.False(
            CatalogueTools.BelongsToAuthority(Copy("South Dublin County Council Libraries"), "dublincity"));
        Assert.False(CatalogueTools.BelongsToAuthority(Copy("Dublin City Libraries"), "southdublin"));
    }

    [Fact]
    public void NoHomeLibraryMeansNothingIsClassifiedAsLocal()
    {
        var belongs = CatalogueTools.BelongsToAuthority(Copy("Dublin City Libraries"), null);
        Assert.False(belongs);
    }

    [Fact]
    public void AnUnrecognisedAuthorityIsNotClaimedAsLocal()
    {
        Assert.False(CatalogueTools.BelongsToAuthority(Copy("Belfast Central Library"), "dublincity"));
        Assert.False(CatalogueTools.BelongsToAuthority(Copy(""), "dublincity"));
    }

    [Fact]
    public void EveryServiceInTheTableMatchesAtLeastOneLiveFeedSpellingOrItsOwnName()
    {
        // Guards against a service being added to the table in a spelling the feed never uses.
        foreach (var key in Authorities.Keys)
        {
            var ownName = Authorities.NameOf(key)!;
            Assert.True(
                CatalogueTools.BelongsToAuthority(Copy(ownName), key),
                $"'{ownName}' does not resolve back to its own key '{key}'");
        }
    }

    private static Holding Copy(string authority, bool available = true) =>
        new("Some Branch", authority, "Adult Fiction", "F", available ? "Available" : "On order",
            null, null, available);
}
