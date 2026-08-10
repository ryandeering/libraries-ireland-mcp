namespace LibrariesIreland.Mcp.Data;

// The 30 library-authority Spydus subdomains.
internal static class Authorities
{
    public const string FederatedHost = "librariesireland.spydus.ie";

    private const int MaxNameLength = 128;

    private sealed record Authority(string Key, string Name, string[] Aliases);
    private static readonly Authority[] All =
    [
        new("carlow", "Carlow County Libraries", ["carlow"]),
        new("cavan", "Cavan County Library", ["cavan"]),
        new("clare", "Clare County Library", ["clare"]),
        new("corkcity", "Cork City Libraries", ["corkcity"]),
        new("corkcoco", "Cork County Library", ["corkcounty", "corkco", "corkcoco"]),
        new("donegal", "Donegal County Library", ["donegal"]),
        new("dublincity", "Dublin City Libraries", ["dublincity", "dcc", "dcpl"]),
        new("dlr", "Dún Laoghaire-Rathdown Libraries",
            ["dlr", "dunlaoghaire", "dunlaoghairerathdown", "dúnlaoghairerathdown", "dúnlaoghaire", "rathdown"]),
        new("fingal", "Fingal Libraries", ["fingal"]),
        new("galway", "Galway County Libraries", ["galway", "galwaycounty", "galwaycity", "gaillimh"]),
        new("kerry", "Kerry County Library", ["kerry"]),
        new("kildare", "Kildare County Library", ["kildare"]),
        new("kilkenny", "Kilkenny County Library", ["kilkenny"]),
        new("laois", "Laois County Library", ["laois"]),
        new("leitrim", "Leitrim County Library", ["leitrim"]),
        new("limerick", "Limerick City and County Libraries",
            ["limerick", "limerickcity", "limerickcounty", "limerickcityandcounty"]),
        new("longford", "Longford County Library", ["longford"]),
        new("louth", "Louth County Library", ["louth"]),
        new("mayo", "Mayo County Library", ["mayo"]),
        new("meath", "Meath County Library", ["meath"]),
        new("monaghan", "Monaghan County Library", ["monaghan"]),
        new("offaly", "Offaly County Library", ["offaly"]),
        new("roscommon", "Roscommon County Library", ["roscommon"]),
        new("sligo", "Sligo County Library", ["sligo"]),
        new("southdublin", "South Dublin Libraries", ["southdublin", "sdcc"]),
        new("tipperary", "Tipperary County Library", ["tipperary", "tipp"]),
        new("waterford", "Waterford City and County Libraries",
            ["waterford", "waterfordcity", "waterfordcounty", "waterfordcityandcounty"]),
        new("westmeath", "Westmeath County Library", ["westmeath"]),
        new("wexford", "Wexford County Library", ["wexford"]),
        new("wicklow", "Wicklow County Library", ["wicklow"]),
    ];

    private static readonly Dictionary<string, Authority> ByKey =
        All.ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);

    // Boilerplate that varies between how the site and a person write the same name.
    private static readonly string[] Noise =
        ["council", "libraries", "library", "services", "service", "the"];

    // County names shared by more than one library service. Greater Dublin alone has four, so a bare
    // "Dublin" must send the caller back to the user rather than pick the biggest one.
    private static readonly HashSet<string> Ambiguous =
        new(StringComparer.OrdinalIgnoreCase) { "dublin", "cork" };

    public static IReadOnlyCollection<string> Keys => ByKey.Keys;

    public static IReadOnlyList<string> Descriptions => [.. All.Select(a => $"{a.Key} ({a.Name})")];

    public static bool IsKnown(string? key) => key is not null && ByKey.ContainsKey(key);

    public static string? NameOf(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var a) ? a.Name : null;

    public static string HostFor(string? authority) =>
        IsKnown(authority) ? $"{authority!.ToLowerInvariant()}.spydus.ie" : FederatedHost;

    public static string? Resolve(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var words = Words(text);
        if (words.Count == 0)
        {
            return null;
        }

        var whole = string.Concat(words);
        if (Ambiguous.Contains(whole))
        {
            return null;
        }

        if (ExactMatch(whole) is { } exact)
        {
            return exact;
        }

        var hits = new List<string>();

        for (var start = 0; start < words.Count; start++)
        {
            var run = string.Empty;
            for (var end = start; end < words.Count; end++)
            {
                run += words[end];
                if (ExactMatch(run) is { } key && !hits.Contains(key))
                {
                    hits.Add(key);
                }
            }
        }

        return hits.Count == 1 ? hits[0] : null;
    }

    private static string? ExactMatch(string candidate)
    {
        foreach (var a in All)
        {
            if (a.Key.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return a.Key;
            }

            foreach (var alias in a.Aliases)
            {
                if (alias.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return a.Key;
                }
            }
        }

        return null;
    }

    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var c in text.Length > MaxNameLength ? text[..MaxNameLength] : text)
        {
            if (char.IsLetterOrDigit(c))
            {
                current.Append(char.ToLowerInvariant(c));
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        words.RemoveAll(Noise.Contains);
        return words;
    }
}
