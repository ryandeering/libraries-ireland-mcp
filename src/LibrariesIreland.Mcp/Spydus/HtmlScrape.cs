namespace LibrariesIreland.Mcp.Spydus;

// Covers the few things RSS cannot supply: the total match count, the branch dropdown and branch
// contact details.
internal static partial class HtmlScrape
{
    public static string? FirstIsbn(string html)
    {
        var field = IsbnField().Match(html);
        if (!field.Success)
        {
            return null;
        }

        var token = IsbnToken().Match(WebUtility.HtmlDecode(field.Groups["v"].Value));
        return token.Success ? token.Groups[1].Value.ToUpperInvariant() : null;
    }

    public static bool IsErrorPage(string html)
    {
        return html.Contains("Error result", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("data-settotal", StringComparison.OrdinalIgnoreCase);
    }

    public static int? TotalMatches(string html)
    {
        var match = SetTotal().Match(html);
        return match.Success && int.TryParse(match.Groups[1].Value, out var total) ? total : null;
    }

    public static List<Branch> BranchList(string html)
    {
        var result = new List<Branch>();

        var select = BiblocSelect().Match(html);
        if (!select.Success)
        {
            return result;
        }

        foreach (Match option in Option().Matches(select.Groups["body"].Value))
        {
            var value = WebUtility.HtmlDecode(option.Groups["val"].Value);
            var star = value.IndexOf('*');

            if (star <= 0)
            {
                continue;
            }

            var irn = value[..star];
            if (!irn.All(char.IsDigit))
            {
                continue;
            }

            var name = WebUtility.HtmlDecode(option.Groups["label"].Value).Trim();
            if (name.Length == 0)
            {
                name = value[(star + 1)..];
            }

            if (IsNotBorrowable(name))
            {
                continue;
            }

            result.Add(new Branch(irn, name));
        }

        return result;
    }

    public static Branch BranchDetail(string html, Branch seed)
    {
        string? address = null;
        string? phone = null;
        string? email = null;

        foreach (Match field in DetailField().Matches(html))
        {
            var value = Text(field.Groups["val"].Value);
            if (value is null)
            {
                continue;
            }

            switch (field.Groups["cap"].Value.Trim().ToLowerInvariant())
            {
                case "address":
                    address ??= value;
                    break;
                case "phone":
                    phone ??= value;
                    break;
                case "email":
                    email ??= value;
                    break;
            }
        }

        var hours = new List<string>();
        foreach (Match hour in OpeningHour().Matches(html))
        {
            var value = Text(hour.Groups["val"].Value);
            if (value is not null)
            {
                hours.Add($"{hour.Groups["day"].Value.Trim()}: {value}");
            }
        }

        var titleMatch = BranchTitle().Match(html);
        var name = titleMatch.Success
            ? WebUtility.HtmlDecode(titleMatch.Groups["name"].Value).Trim()
            : seed.Name;

        return seed with
        {
            Name = name.Length > 0 ? name : seed.Name,
            Address = address,
            Phone = phone,
            Email = email,
            OpeningHours = hours.Count > 0 ? hours : null,
        };
    }

    private static string? Text(string html)
    {
        var stripped = WebUtility.HtmlDecode(AnyTag().Replace(html, " "));
        var collapsed = string.Join(
            ' ', stripped.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return collapsed.Length == 0 ? null : collapsed;
    }

    private static bool IsNotBorrowable(string name)
    {
        ReadOnlySpan<string> noise =
        [
            "DO NOT USE", "Default Cleanup", "Staff Library", " HQ", "HQ ", " Store",
            "eLibrary", "Civics Locker", "Temporarily Closed", "Test ", "Withdrawn",
        ];

        foreach (var fragment in noise)
        {
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"data-settotal=""(\d+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SetTotal();

    [GeneratedRegex(
        @"<select[^>]*name=""BIBLOC""[^>]*>(?<body>.*?)</select>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BiblocSelect();

    [GeneratedRegex(
        @"<option[^>]*value=""(?<val>[^""]*)""[^>]*>(?<label>[^<]*)</option>",
        RegexOptions.IgnoreCase)]
    private static partial Regex Option();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(
        @"ISBN:\s*</span>\s*</div>\s*<div[^>]*>(?:\s*<span[^>]*>)?(?<v>[^<]+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex IsbnField();

    [GeneratedRegex(@"\b(\d{9}[\dXx]|\d{13})\b")]
    private static partial Regex IsbnToken();

    [GeneratedRegex(
        @"<span class=""Style1"">(?<cap>[^<]+?):\s*</span>(?<val>.*?)(?=<span class=""Style1"">|</div>|</p>)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DetailField();

    [GeneratedRegex(
        @"<span class=""openinghours-day"">(?<day>[^<]+)</span>(?<val>[^<]*)",
        RegexOptions.IgnoreCase)]
    private static partial Regex OpeningHour();

    [GeneratedRegex(
        @"<h1[^>]*class=""card-title""[^>]*>\s*<span[^>]*>(?<name>[^<]+)</span>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BranchTitle();
}
