namespace LibrariesIreland.Mcp.Spydus;

internal static class QueryBuilder
{
    // Grammar, reverse-engineered from the queries the site generates for its own advanced search
    // form: "+" is AND, "/" is OR, "-" is NOT, parentheses group, and a term is "INDEX: VALUE".
    // A multi-word keyword becomes an AND group, as in "SU: (SCIENCE + FICTION)". There is no
    // documentation for any of this and no escape syntax, which is why user text has operator
    // characters removed rather than escaped.
    //
    // The index names are not guessable: title is TICN, author AUCN, anywhere BSOPAC.
    public const string Anywhere = "BSOPAC";
    public const string TitleIdx = "TICN";
    public const string AuthorIdx = "AUCN";
    public const string SubjectIdx = "SU";
    public const string SeriesIdx = "SE";
    public const string PublisherIdx = "PU";
    public const string IsbnIdx = "ISBN";

    private const string AvailableItemBody =
        "ITMRCVF: 1 - MINOR: (06701 / 08301 / ITR05) - DYN: IST + FILTER: 1";

    private const string NonFictionExpr = "DYN: FBK - FIC: (C / F / J / 1)";
    private const string FictionExpr = "FIC: (C / F / J / 1)";

    private const int MaxWordsPerTerm = 12;
    private const int MaxIdentifierLength = 20;
    private const int MaxWordLength = 64;

    public static string Available() => $"BIBISSITM> ({AvailableItemBody})";

    public static string AvailableAtBranch(string branchIrn)
    {
        var scoped = $"ITMLOC: {branchIrn} + {AvailableItemBody}";
        return $"BIBISSITM> ({scoped})";
    }

    public static string? NormaliseBrn(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxIdentifierLength)
        {
            return null;
        }

        return trimmed.All(char.IsDigit) ? trimmed : null;
    }

    public static string? NormaliseBranchIrn(string? value) => NormaliseBrn(value);

    public static string? NormaliseIsbn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = new string([.. value.Where(c => c is not ('-' or ' '))]).ToUpperInvariant();

        if (compact.Length == 13 && compact.All(char.IsDigit) && Isbn13CheckDigitIsValid(compact))
        {
            return compact;
        }

        if (compact.Length == 10 && compact[..9].All(char.IsDigit)
            && (char.IsDigit(compact[9]) || compact[9] == 'X')
            && Isbn10CheckDigitIsValid(compact))
        {
            return compact;
        }

        return null;
    }

    public static string ByBrn(string brn) => $"BRN: {brn}";

    public static string MaterialExpr(MaterialType type)
    {
        return type switch
        {
            MaterialType.Books => "BFRMT: BK",
            MaterialType.AudioBooks => "BIBCGF: ABOOK",
            MaterialType.SoundRecordings => "BTYP: (I / J)",
            MaterialType.MusicScores => "BTYP: (C / D)",
            MaterialType.Video => "BFRMT: VM",
            MaterialType.Journals => "BFRMT: SE",
            _ => string.Empty,
        };
    }

    public static string ContentExpr(ContentKind kind)
    {
        return kind switch
        {
            ContentKind.NonFiction => NonFictionExpr,
            ContentKind.Fiction => FictionExpr,
            _ => string.Empty,
        };
    }

    public static string AudienceExpr(Audience audience)
    {
        return audience switch
        {
            Audience.Adult => "FORMAT: BIB - AUD: (J / A / B / C / D) + DYN: 021",
            Audience.Youth => "AUD: (C / D)",
            Audience.Children => "AUD: (J / A / B)",
            _ => string.Empty,
        };
    }

    private static readonly Dictionary<string, string> LanguageCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ENGLISH"] = "ENG",
            ["EN"] = "ENG",
            ["IRISH"] = "GLE",
            ["GAEILGE"] = "GLE",
            ["GAELIC"] = "GLE",
            ["GA"] = "GLE",
            ["FRENCH"] = "FRE",
            ["FR"] = "FRE",
            ["GERMAN"] = "GER",
            ["DE"] = "GER",
            ["SPANISH"] = "SPA",
            ["ES"] = "SPA",
            ["ITALIAN"] = "ITA",
            ["IT"] = "ITA",
            ["POLISH"] = "POL",
            ["PL"] = "POL",
            ["PORTUGUESE"] = "POR",
            ["PT"] = "POR",
            ["RUSSIAN"] = "RUS",
            ["RU"] = "RUS",
            ["ROMANIAN"] = "RUM",
            ["RO"] = "RUM",
            ["CHINESE"] = "CHI",
            ["ZH"] = "CHI",
            ["ARABIC"] = "ARA",
            ["AR"] = "ARA",
            ["UKRAINIAN"] = "UKR",
            ["UK"] = "UKR",
            ["LITHUANIAN"] = "LIT",
            ["LT"] = "LIT",
            ["LATVIAN"] = "LAV",
            ["LV"] = "LAV",
            ["DUTCH"] = "DUT",
            ["NL"] = "DUT",
        };

    public static string? LanguageCode(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var letters = new string([.. language.Where(char.IsLetter)]).ToUpperInvariant();

        if (LanguageCodes.TryGetValue(letters, out var mapped))
        {
            return mapped;
        }


        return letters.Length == 3 ? letters : null;
    }

    public static string AuthorTerm(string? text)
    {
        var words = Words(text);
        if (words.Count == 0)
        {
            return string.Empty;
        }

        if (words.Count == 1)
        {
            return $"{AuthorIdx}: {words[0]}";
        }

        var commaGiven = text!.Contains(',', StringComparison.Ordinal);

        if (commaGiven)
        {
            return $"{AuthorIdx}: '{string.Join(" ", words)}'";
        }

        if (words.Count == 2)
        {
            return $"{AuthorIdx}: '{words[1]} {words[0]}'";
        }

        return Term(AuthorIdx, text);
    }

    public static string Term(string index, string? text)
    {
        var words = Words(text);
        if (words.Count == 0)
        {
            return string.Empty;
        }

        return words.Count == 1
            ? $"{index}: {words[0]}"
            : $"{index}: ({string.Join(" + ", words)})";
    }

    public static string YearRange(int? from, int? to)
    {
        if (from is null && to is null)
        {
            return string.Empty;
        }

        var low = from ?? 1000;
        var high = to ?? 2999;
        if (low > high)
        {
            (low, high) = (high, low);
        }

        return $"PD: \"{low} - {high}\"";
    }

    public static string And(params ReadOnlySpan<string> parts)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(" + ");
            }

            sb.Append(part);
        }

        return sb.ToString();
    }

    private static bool Isbn13CheckDigitIsValid(string isbn)
    {
        var sum = 0;
        for (var i = 0; i < 13; i++)
        {
            sum += (isbn[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return sum % 10 == 0;
    }

    private static bool Isbn10CheckDigitIsValid(string isbn)
    {
        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            var value = isbn[i] == 'X' ? 10 : isbn[i] - '0';
            sum += value * (10 - i);
        }

        return sum % 11 == 0;
    }

    private static char Fold(char c)
    {
        return c switch
        {
            'á' or 'à' or 'â' or 'ä' or 'ã' or 'å' => 'a',
            'Á' or 'À' or 'Â' or 'Ä' or 'Ã' or 'Å' => 'A',
            'é' or 'è' or 'ê' or 'ë' => 'e',
            'É' or 'È' or 'Ê' or 'Ë' => 'E',
            'í' or 'ì' or 'î' or 'ï' => 'i',
            'Í' or 'Ì' or 'Î' or 'Ï' => 'I',
            'ó' or 'ò' or 'ô' or 'ö' or 'õ' or 'ø' => 'o',
            'Ó' or 'Ò' or 'Ô' or 'Ö' or 'Õ' or 'Ø' => 'O',
            'ú' or 'ù' or 'û' or 'ü' => 'u',
            'Ú' or 'Ù' or 'Û' or 'Ü' => 'U',
            'ý' or 'ÿ' => 'y',
            'Ý' => 'Y',
            'ñ' => 'n',
            'Ñ' => 'N',
            'ç' => 'c',
            'Ç' => 'C',
            _ => c,
        };
    }

    private static List<string> Words(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var sb = new StringBuilder();
        foreach (var raw in text)
        {
            var c = Fold(raw);
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(char.ToUpperInvariant(c));
            }
            else if (sb.Length > 0)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }

            if (sb.Length > MaxWordLength)
            {
                sb.Length = MaxWordLength;
            }

            if (result.Count >= MaxWordsPerTerm)
            {
                return result;
            }
        }

        if (sb.Length > 0)
        {
            result.Add(sb.ToString());
        }

        return result;
    }
}
