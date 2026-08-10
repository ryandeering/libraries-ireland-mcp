namespace LibrariesIreland.Mcp.Tests;

public class QueryBuilderTests
{
    [Fact]
    public void SingleWordTermIsAPlainIndexLookup()
        => Assert.Equal("SU: YUGOSLAVIA", QueryBuilder.Term(QueryBuilder.SubjectIdx, "Yugoslavia"));

    [Fact]
    public void MultiWordTermBecomesAnAndGroup()
    {
        Assert.Equal("TICN: (FALL + OF + YUGOSLAVIA)",
            QueryBuilder.Term(QueryBuilder.TitleIdx, "fall of Yugoslavia"));
    }

    [Theory]
    [InlineData("dune + BFRMT: XX", "BSOPAC: (DUNE + BFRMT + XX)")]
    [InlineData("O'Brien", "BSOPAC: (O + BRIEN)")]
    [InlineData("cats / dogs - birds", "BSOPAC: (CATS + DOGS + BIRDS)")]
    [InlineData("(nested)", "BSOPAC: NESTED")]
    public void OperatorCharactersInUserInputCannotAlterTheExpression(
        string input, string expected)
    {
        var actual = QueryBuilder.Term(QueryBuilder.Anywhere, input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Ó Cadhain", "BSOPAC: (O + CADHAIN)")]
    [InlineData("Dún Laoghaire", "BSOPAC: (DUN + LAOGHAIRE)")]
    [InlineData("Seán Ó Faoláin", "BSOPAC: (SEAN + O + FAOLAIN)")]
    [InlineData("café", "BSOPAC: CAFE")]
    public void AccentedLettersAreFoldedToAscii(string input, string expected) =>
        Assert.Equal(expected, QueryBuilder.Term(QueryBuilder.Anywhere, input));

    [Fact]
    public void EmptyAndPunctuationOnlyInputYieldsNoTerm()
    {
        Assert.Equal("", QueryBuilder.Term(QueryBuilder.Anywhere, null));
        Assert.Equal("", QueryBuilder.Term(QueryBuilder.Anywhere, "   "));
        Assert.Equal("", QueryBuilder.Term(QueryBuilder.Anywhere, "+++ /// ---"));
    }

    [Fact]
    public void AndSkipsEmptyFragments()
    {
        Assert.Equal("SU: X + BFRMT: BK",
            QueryBuilder.And("SU: X", "", QueryBuilder.MaterialExpr(MaterialType.Books), ""));
        Assert.Equal("", QueryBuilder.And("", "", ""));
    }

    [Fact]
    public void AvailableAtBranchUsesTheItemLevelIndex()
    {
        var q = QueryBuilder.AvailableAtBranch("25772");
        Assert.StartsWith("BIBISSITM> (ITMLOC: 25772", q);
        Assert.Contains("ITMRCVF: 1", q);
    }

    [Theory]
    [InlineData("12345", "12345")]
    [InlineData("  12345  ", "12345")]
    public void AValidBrnIsAccepted(string input, string expected)
    {
        var actual = QueryBuilder.NormaliseBrn(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("12x34")]
    [InlineData("12345 + DYN: X")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("123456789012345678901")]
    public void AnInvalidBrnIsRejectedRatherThanRepaired(string? input) =>
        // Stripping the non-digits from "12x34" gives "1234", a different and perfectly valid
        // record, so the caller would be handed the wrong book with nothing to signal it.
        Assert.Null(QueryBuilder.NormaliseBrn(input));

    [Theory]
    [InlineData("9780575081505", "9780575081505")]
    [InlineData("978-0-575-08150-5", "9780575081505")]
    [InlineData("978 0 575 08150 5", "9780575081505")]
    [InlineData("080442957X", "080442957X")]
    [InlineData("0-8044-2957-x", "080442957X")]
    public void AnIsbnIsNormalisedToASingleToken(string input, string expected) =>
        // The tool advertises "with or without hyphens", and a hyphenated ISBN previously went
        // through the word splitter as "ISBN: (978 + 0 + 575 + 08150 + 5)", which matches nothing.
        Assert.Equal(expected, QueryBuilder.NormaliseIsbn(input));

    [Theory]
    [InlineData("12345")]
    [InlineData("97805750815050000")]
    [InlineData("978057508150X")]
    [InlineData("not-an-isbn")]
    [InlineData("")]
    [InlineData(null)]
    public void AnInvalidIsbnIsRejected(string? input) => Assert.Null(QueryBuilder.NormaliseIsbn(input));

    [Theory]
    [InlineData("9780575081506")]
    [InlineData("9781805262863")]
    [InlineData("0804429571")]
    public void AnIsbnWithABadCheckDigitIsRejected(string input) =>
        Assert.Null(QueryBuilder.NormaliseIsbn(input));

    [Fact]
    public void YearRangeOrdersItsBounds()
    {
        Assert.Equal("PD: \"1990 - 1999\"", QueryBuilder.YearRange(1990, 1999));
        Assert.Equal("PD: \"1990 - 1999\"", QueryBuilder.YearRange(1999, 1990));
        Assert.Equal("", QueryBuilder.YearRange(null, null));
    }

    [Theory]
    [InlineData("English", "ENG")]
    [InlineData("irish", "GLE")]
    [InlineData("Gaeilge", "GLE")]
    [InlineData("pol", "POL")]
    [InlineData("Polish", "POL")]
    public void LanguageNamesMapToMarcCodes(string input, string expected)
        => Assert.Equal(expected, QueryBuilder.LanguageCode(input));

    [Theory]
    [InlineData("en", "ENG")]
    [InlineData("ga", "GLE")]
    [InlineData("fr", "FRE")]
    [InlineData("NL", "DUT")]
    public void TwoLetterIsoCodesAreAccepted(string input, string expected) =>
        Assert.Equal(expected, QueryBuilder.LanguageCode(input));

    [Fact]
    public void UnknownLanguageIsNullRatherThanABrokenFilter()
        => Assert.Null(QueryBuilder.LanguageCode("Klingon"));

    [Fact]
    public void NonFictionUsesTheCataloguesOwnDefinition()
        => Assert.Equal("DYN: FBK - FIC: (C / F / J / 1)", QueryBuilder.ContentExpr(ContentKind.NonFiction));
}

public class AuthorityTests
{
    [Theory]
    [InlineData("Dublin City", "dublincity")]
    [InlineData("dublincity", "dublincity")]
    [InlineData("Dublin City Libraries", "dublincity")]
    [InlineData("DLR", "dlr")]
    [InlineData("Cork County Library", "corkcoco")]
    [InlineData("Cork City Libraries", "corkcity")]
    [InlineData("fingal", "fingal")]
    public void FreeTextResolvesToAnAuthorityKey(string input, string expected)
        => Assert.Equal(expected, Authorities.Resolve(input));

    [Theory]
    [InlineData("Cork County Council Libraries", "corkcoco")]
    [InlineData("Cork City", "corkcity")]
    [InlineData("South Dublin", "southdublin")]
    [InlineData("Dun Laoghaire", "dlr")]
    [InlineData("Dún Laoghaire-Rathdown", "dlr")]
    public void CityAndCountyServicesAreNeverConfused(string input, string expected)
    {
        var actual = Authorities.Resolve(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AmbiguousInputResolvesToNullRatherThanPickingOne()
    {
        // "Cork" could be either Cork service, and "Dublin" could be any of four.
        Assert.Null(Authorities.Resolve("Cork"));
        Assert.Null(Authorities.Resolve("Dublin"));
    }

    [Theory]
    [InlineData("Cork County or Cork City")]
    [InlineData("Dublin City or Fingal")]
    [InlineData("Cork City and Cork County")]
    public void APhraseNamingTwoServicesResolvesToNeither(string input) =>
        // Prefix matching over the squashed string picked the first alias that matched, so a
        // question mentioning two services silently chose one of them.
        Assert.Null(Authorities.Resolve(input));

    [Fact]
    public void AServiceWhoseNameContainsAnotherIsNotConfusedWithIt()
    {
        // "meath" is a substring of "westmeath", which is why matching runs on whole words.
        Assert.Equal("westmeath", Authorities.Resolve("Westmeath County Council Libraries"));
        Assert.Equal("meath", Authorities.Resolve("Meath County Library"));
    }

    [Fact]
    public void UnknownAuthorityResolvesToNullSoTheToolCanAsk()
    {
        Assert.Null(Authorities.Resolve("Belfast"));
        Assert.Null(Authorities.Resolve(""));
        Assert.Null(Authorities.Resolve(null));
    }

    [Fact]
    public void KnownAuthorityScopesToItsOwnSubdomain()
    {
        // Only the subdomain filters the holdings list; the federated host does not.
        Assert.Equal("dublincity.spydus.ie", Authorities.HostFor("dublincity"));
        Assert.Equal(Authorities.FederatedHost, Authorities.HostFor(null));
        Assert.Equal(Authorities.FederatedHost, Authorities.HostFor("nonsense"));
    }
}
