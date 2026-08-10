namespace LibrariesIreland.Mcp.Tests;

// The feed spells an authority differently from this server's own table, and neither string
// contains the other, so every spelling the live feed uses has to resolve through the canonical key.
public class AuthorityResolutionTests
{

    [Theory]
    [MemberData(nameof(Fixtures.LiveAuthorityNames), MemberType = typeof(Fixtures))]
    public void EveryAuthorityNameInTheLiveFeedResolvesToAKnownKey(string feedName)
    {
        var key = Authorities.Resolve(feedName);
        Assert.True(key is not null, $"'{feedName}' did not resolve to any authority key");
        Assert.True(Authorities.IsKnown(key), $"'{feedName}' resolved to unknown key '{key}'");
    }

    [Theory]
    [InlineData("Wicklow County Libraries", "wicklow")]
    [InlineData("Fingal County Council Libraries", "fingal")]
    [InlineData("Clare County Council Libraries", "clare")]
    [InlineData("Dun Laoghaire-Rathdown County Council Libraries", "dlr")]
    [InlineData("South Dublin County Council Libraries", "southdublin")]
    [InlineData("Cork County Council Libraries", "corkcoco")]
    [InlineData("Cork City Libraries", "corkcity")]
    [InlineData("Waterford Libraries", "waterford")]
    [InlineData("Limerick City and County Council Libraries", "limerick")]
    [InlineData("Dublin City Libraries", "dublincity")]
    public void FeedSpellingMapsToTheSameKeyAsTheConfiguredSpelling(
        string feed, string key) => Assert.Equal(key, Authorities.Resolve(feed));

    [Fact]
    public void SouthDublinIsNeverMistakenForDublinCity()
    {
        Assert.NotEqual(
            Authorities.Resolve("Dublin City Libraries"),
            Authorities.Resolve("South Dublin County Council Libraries"));
    }
}
