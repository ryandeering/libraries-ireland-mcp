namespace LibrariesIreland.Mcp.Tools;

internal sealed class CatalogueTools(SpydusClient client, HomeLibraryStore home)
{
    internal const string BorrowGuidance =
        "Reservations are free, and are placed on the Libraries Ireland website rather than through " +
        "this tool. Open the catalogue link, sign in with your library card number and PIN, then " +
        "choose the branch you want to collect from. Items can be sent from any public library in " +
        "Ireland to your local branch, and transfers run twice a week, so allow about a week. Most " +
        "cards allow up to 12 reservations at a time.";

    private const int MaxLimit = 50;

    private static string NoHomeLibraryNote =>
        "No home library is configured yet. Ask the user which library service they belong to (one " +
        $"of: {string.Join(", ", Authorities.Keys)}), then call set_home_library. Otherwise re-run " +
        "with scope=Nationwide to search every library in Ireland.";

    [Description(
        "Search the Libraries Ireland public library catalogue. Combine any of the text fields with " +
        "the filters. Defaults to the user's home library service; pass scope=Nationwide to search " +
        "all Irish public libraries. Returns catalogue records with availability. It does not rank " +
        "by quality, so judge that yourself from the titles, authors and dates returned.")]
    public async Task<SearchResponse> SearchCatalogueAsync(
        [Description("Free-text search across the whole record: title, author, contents, notes.")]
        string? query = null,
        [Description("Words from the title.")] string? title = null,
        [Description("Author name, such as 'Frank Herbert' or 'Herbert'.")] string? author = null,
        [Description("Subject or topic heading, such as 'Yugoslavia' or 'organic chemistry'.")]
        string? subject = null,
        [Description("Series title, such as 'Discworld'.")] string? series = null,
        [Description("ISBN, with or without hyphens.")] string? isbn = null,
        [Description("Which libraries to search. Defaults to the user's home library service.")]
        Scope scope = Scope.HomeLibrary,
        [Description("Format of item. Defaults to Books.")]
        MaterialType materialType = MaterialType.Books,
        [Description("Restrict to non-fiction or fiction. Use NonFiction for 'a book about X'.")]
        ContentKind contentKind = ContentKind.Any,
        [Description("Intended readership.")] Audience audience = Audience.Any,
        [Description("Language name or MARC code, such as 'English', 'Irish' or 'pol'.")]
        string? language = null,
        [Description("Earliest publication year.")] int? publishedFrom = null,
        [Description("Latest publication year.")] int? publishedTo = null,
        [Description("Only return titles with a copy on the shelf right now.")]
        bool availableNow = false,
        [Description("Maximum records to return, from 1 to 50.")] int limit = 20,
        CancellationToken ct = default)
    {
        var isbnTerm = string.Empty;
        if (!string.IsNullOrWhiteSpace(isbn))
        {
            var normalised = QueryBuilder.NormaliseIsbn(isbn);
            if (normalised is null)
            {
                return new SearchResponse(
                    string.Empty, "none", 0, [],
                    $"'{isbn}' is not a valid ISBN. Give 10 or 13 digits, with or without hyphens.");
            }

            isbnTerm = $"{QueryBuilder.IsbnIdx}: {normalised}";
        }

        if (!string.IsNullOrWhiteSpace(language) && QueryBuilder.LanguageCode(language) is null)
        {
            return new SearchResponse(
                string.Empty, "none", 0, [],
                $"'{language}' is not a recognised language. Give a name such as 'Irish', a "
                + "two-letter code such as 'ga', or a MARC three-letter code such as 'gle'.");
        }

        var terms = QueryBuilder.And(
            QueryBuilder.Term(QueryBuilder.Anywhere, query),
            QueryBuilder.Term(QueryBuilder.TitleIdx, title),
            QueryBuilder.AuthorTerm(author),
            QueryBuilder.Term(QueryBuilder.SubjectIdx, subject),
            QueryBuilder.Term(QueryBuilder.SeriesIdx, series),
            isbnTerm);

        if (terms.Length == 0)
        {
            return new SearchResponse(
                string.Empty,
                "none",
                0,
                [],
                "Provide at least one of: query, title, author, subject, series, isbn.");
        }

        return await RunSearchAsync(
            terms, scope, materialType, contentKind, audience, language,
            publishedFrom, publishedTo, availableNow, limit, ct);
    }

    [Description(
        "Find books on a topic that the user can actually borrow. Use this when asked for a good " +
        "book about something. Searches subject headings in the user's home library service and " +
        "returns a broad candidate set with authors and dates, deliberately unranked. Assess which " +
        "titles are worth reading yourself, searching the web if that helps, then tell the user " +
        "which of these they can get.")]
    public async Task<SearchResponse> BrowseSubjectAsync(
        [Description("The topic, such as 'Yugoslavia', 'beekeeping' or 'Roman Britain'.")]
        string subject,
        [Description("Which libraries to search. Defaults to the user's home library service.")]
        Scope scope = Scope.HomeLibrary,
        [Description("Restrict to non-fiction or fiction. Defaults to NonFiction, which is usually "
                     + "what 'a book about X' means. Pass Any to include novels set in that place.")]
        ContentKind contentKind = ContentKind.NonFiction,
        [Description("Only return titles with a copy on the shelf right now.")]
        bool availableNow = false,
        [Description("Maximum records to return, from 1 to 50.")] int limit = 30,
        CancellationToken ct = default)
    {
        var terms = QueryBuilder.Term(QueryBuilder.SubjectIdx, subject);
        if (terms.Length == 0)
        {
            return new SearchResponse(string.Empty, "none", 0, [], "Provide a subject to browse.");
        }

        return await RunSearchAsync(
            terms, scope, MaterialType.Books, contentKind, Audience.Any,
            null, null, null, availableNow, limit, ct);
    }

    [Description(
        "Full detail for one catalogue record, including every copy in every branch that holds it. " +
        "Takes the BRN returned by a search.")]
    public async Task<BookDetail> GetBookAsync(
        [Description("The record's BRN, as returned by a search.")] string brn,
        [Description("Whose holdings to list. Nationwide shows every library in Ireland.")]
        Scope scope = Scope.Nationwide,
        CancellationToken ct = default)
    {
        var host = HostFor(scope);
        var id = QueryBuilder.NormaliseBrn(brn);
        if (id is null)
        {
            return new BookDetail(
                brn, "(invalid BRN)", null, null, null, null, null, 0, 0, [],
                SpydusClient.RecordUrl(host, "0"),
                $"'{brn}' is not a valid BRN. A BRN is the numeric record id returned by a search.");
        }

        var qry = QueryBuilder.ByBrn(id);
        var url = SpydusClient.RecordUrl(host, id);

        var rss = await client.SearchRssAsync(host, qry, 2, ct);
        var record = RssParser.Parse(rss).FirstOrDefault();

        if (record is null)
        {
            return new BookDetail(
                brn, "(not found)", null, null, null, null, null, 0, 0, [], url,
                "No record with that BRN was found in this scope.");
        }

        // The record page carries the identifiers RSS omits. The ISBN matters most, because it is
        // what makes a title findable in reviews and elsewhere online.
        string? isbn = null;
        try
        {
            var html = await client.RecordPageAsync(host, record.Brn, ct);
            isbn = HtmlScrape.FirstIsbn(html);
        }
        catch (Exception ex) when (IsOptionalFetchFailure(ex, ct))
        {
            // The detail is still worth returning without the ISBN.
        }

        return new BookDetail(
            record.Brn,
            record.Title,
            record.Author,
            record.Published,
            isbn,
            record.Summary,
            isbn is null ? null : CoverUrl(isbn),
            record.Holdings.Count > 0 ? record.Holdings.Count : record.CopiesReported,
            record.Holdings.Count(h => h.Available),
            record.Holdings,
            url,
            BorrowGuidance);
    }

    [Description(
        "Answer 'can I actually get this book, and how?' for one record. Reports whether a copy is " +
        "on the shelf at the user's own branch, elsewhere in their library service, or only in " +
        "another county, in which case it can be requested and sent to them.")]
    public async Task<AvailabilityReport> WhereCanIGetThisAsync(
        [Description("The record's BRN, as returned by a search.")] string brn,
        CancellationToken ct = default)
    {
        var id = QueryBuilder.NormaliseBrn(brn);
        if (id is null)
        {
            return new AvailabilityReport(
                brn, "(invalid BRN)",
                $"'{brn}' is not a valid BRN. A BRN is the numeric record id returned by a search.",
                false, false, false, [], [], [], BorrowGuidance,
                SpydusClient.RecordUrl(Authorities.FederatedHost, "0"), null);
        }

        var qry = QueryBuilder.ByBrn(id);
        var url = SpydusClient.RecordUrl(Authorities.FederatedHost, id);

        var rss = await client.SearchRssAsync(Authorities.FederatedHost, qry, 2, ct);
        var record = RssParser.Parse(rss).FirstOrDefault();

        if (record is null)
        {
            return new AvailabilityReport(
                brn, "(not found)", "No record with that BRN was found.",
                false, false, false, [], [], [], BorrowGuidance, url,
                "Check the BRN, or search again.");
        }

        var homeName = Authorities.NameOf(home.Current.Authority);
        var branchName = home.Current.BranchName;

        var mine = record.Holdings.Where(IsHomeAuthority).ToList();
        var others = record.Holdings.Except(mine).ToList();

        var onShelfAtBranch = branchName is { Length: > 0 }
            && mine.Any(h => h.Available && Matches(h.Branch, branchName));
        var onShelfInAuthority = mine.Any(h => h.Available);

        var otherAuthorities = others
            .Select(h => h.Authority)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(a => a!)
            .ToList();

        string verdict;
        string? note = null;

        if (!home.IsConfigured)
        {
            var availableNow = record.Holdings.Count(h => h.Available);
            verdict = record.Holdings.Count == 0 && record.CopiesReported == 0
                ? "No library in Ireland holds this."
                : $"Held by {Count(otherAuthorities.Count, "library service")} nationwide, with "
                  + $"{Count(availableNow, "copy", "copies")} on the shelf now.";
            note = NoHomeLibraryNote;
        }
        else if (onShelfAtBranch)
        {
            verdict = $"On the shelf now at {branchName}. You can go and borrow it.";
        }
        else if (onShelfInAuthority)
        {
            var where = mine.First(h => h.Available).Branch;
            verdict = $"On the shelf now at {where}, in your own library service. You can reserve "
                      + "it for collection at your usual branch.";
        }
        else if (mine.Count > 0)
        {
            verdict = $"{homeName} holds {Count(mine.Count, "copy", "copies")}, but none are on the "
                      + "shelf right now. Reserve it and you will be told when one is returned.";
        }
        else if (others.Count > 0)
        {
            var otherServicesText = otherAuthorities.Count == 1
                ? "1 other library service has it"
                : $"{otherAuthorities.Count} other library services have it";
            verdict = $"Not held by {homeName}, but {otherServicesText}. Reserve it and it will be sent to "
                      + "your local branch, usually within a week.";
        }
        else if (record.CopiesReported > 0)
        {
            verdict = $"{Count(record.CopiesReported, "copy", "copies")} held across the country, "
                      + "but the catalogue did not break them down by branch, so this cannot say "
                      + "which service holds them. Open the catalogue link to see for yourself.";
        }
        else
        {
            verdict = "No copies are recorded in any Irish public library.";
        }

        return new AvailabilityReport(
            record.Brn, record.Title, verdict,
            onShelfAtBranch, onShelfInAuthority, mine.Count > 0,
            mine, others, otherAuthorities, BorrowGuidance, url, note);
    }

    internal static bool BelongsToAuthority(Holding holding, string? authorityKey)
    {
        return authorityKey is not null
            && string.Equals(
                Authorities.Resolve(holding.Authority), authorityKey, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SearchResponse> RunSearchAsync(
        string terms, Scope scope, MaterialType material, ContentKind content, Audience audience,
        string? language, int? from, int? to, bool availableNow, int limit,
        CancellationToken ct)
    {
        if (scope == Scope.HomeLibrary && !home.IsConfigured)
        {
            return new SearchResponse(terms, "unknown", null, [], NoHomeLibraryNote);
        }

        limit = Math.Clamp(limit, 1, MaxLimit);

        var host = HostFor(scope);
        var lang = QueryBuilder.LanguageCode(language);
        var branchIrn = QueryBuilder.NormaliseBranchIrn(home.Current.BranchIrn);

        // Branch-level availability is very restrictive, so it only applies when the user has named
        // a usual branch. Otherwise availability means anywhere in the chosen scope.
        var availability = string.Empty;
        if (availableNow)
        {
            availability = scope == Scope.HomeLibrary && !string.IsNullOrWhiteSpace(branchIrn)
                ? QueryBuilder.AvailableAtBranch(branchIrn)
                : QueryBuilder.Available();
        }

        var qry = QueryBuilder.And(
            terms,
            QueryBuilder.MaterialExpr(material),
            QueryBuilder.ContentExpr(content),
            QueryBuilder.AudienceExpr(audience),
            lang is null ? string.Empty : $"LANG: {lang}",
            QueryBuilder.YearRange(from, to),
            availability);

        var scopeText = scope == Scope.Nationwide
            ? "all public libraries in Ireland"
            : home.Description;

        var rss = await client.SearchRssAsync(host, qry, limit, ct);
        var records = RssParser.Parse(rss);

        int? total = null;
        try
        {
            var html = await client.SearchHtmlAsync(host, qry, ct);
            if (HtmlScrape.IsErrorPage(html))
            {
                return new SearchResponse(
                    qry, scopeText, 0, [],
                    $"Nothing matched in {scopeText}. The catalogue answers the same way when a "
                    + "filter is not supported, so if you combined unusual ones try dropping one, "
                    + "and otherwise broaden the search or use scope=Nationwide.");
            }

            total = HtmlScrape.TotalMatches(html);
        }
        catch (Exception ex) when (IsOptionalFetchFailure(ex, ct))
        {
            // The count is a nicety; the results themselves matter more.
        }

        var results = records.Select(r => ToSummary(r, scope)).ToList();
        string? note = null;

        if (results.Count == 0)
        {
            note = $"Nothing matched in {scopeText}. Try broadening the filters, or scope=Nationwide.";
        }
        else if (total is int matched && matched > results.Count)
        {
            note = $"Showing {results.Count} of {matched} matches. Raise 'limit' or narrow the "
                   + "filters to see more.";
        }

        return new SearchResponse(qry, scopeText, total, results, note);
    }

    private BookSummary ToSummary(RawRecord record, Scope scope)
    {
        var available = record.Holdings.Count(h => h.Available);
        var authorities = record.Holdings
            .Select(h => h.Authority)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var branchName = home.Current.BranchName;
        var atHomeBranch = branchName is { Length: > 0 }
            && record.Holdings.Any(h => h.Available && Matches(h.Branch, branchName));

        string note;
        if (atHomeBranch)
        {
            note = $"On the shelf now at {branchName}.";
        }
        else if (available > 0)
        {
            var where = record.Holdings.First(h => h.Available).Branch;
            note = available == 1
                ? $"One copy on the shelf now, at {where}."
                : $"{available} copies on the shelf now, including {where}.";
        }
        else if (record.Holdings.Count > 0)
        {
            note = record.Holdings.Count == 1
                ? "The only copy is out or in transit, but it can be reserved."
                : $"All {record.Holdings.Count} copies are out or in transit, but it can be reserved.";
            if (record.Reserves > 0)
            {
                note += $" {Count(record.Reserves, "reservation")} already queued.";
            }
        }
        else if (record.CopiesReported > 0)
        {
            note = $"{Count(record.CopiesReported, "copy", "copies")} held, but the catalogue did "
                   + "not break them down by branch, so shelf status is unknown. Open the "
                   + "catalogue link for per-branch availability.";
        }
        else
        {
            note = scope == Scope.Nationwide
                ? "No copies recorded."
                : "No copies in your library service. Try scope=Nationwide to request one from "
                  + "another county.";
        }

        var total = record.Holdings.Count > 0 ? record.Holdings.Count : record.CopiesReported;

        return new BookSummary(
            record.Brn, record.Title, record.Author, record.Published, record.Summary,
            total, available, authorities, note,
            SpydusClient.RecordUrl(HostFor(scope), record.Brn));
    }

    private string HostFor(Scope scope) => scope == Scope.Nationwide ? Authorities.FederatedHost : home.Host;

    private static string Count(int n, string singular, string? plural = null)
    {
        var noun = n == 1 ? singular : plural ?? singular + "s";
        return $"{n} {noun}";
    }

    // Both enrichment fetches are optional: the RSS result is already in hand and is what the user
    // asked for. A timeout from HttpClient surfaces as TaskCanceledException rather than
    // HttpRequestException, so catching only the latter threw away a good answer whenever the
    // optional request was slow. A cancellation the caller actually requested is still allowed to
    // propagate, because that is not a failure to absorb.
    private static bool IsOptionalFetchFailure(Exception ex, CancellationToken ct)
    {
        return ex is HttpRequestException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested);
    }

    private bool IsHomeAuthority(Holding holding) => BelongsToAuthority(holding, home.Current.Authority);

    // Loose containment in either direction, because branch names carry their authority as a prefix
    // and the two sources abbreviate it differently.
    private static bool Matches(string? a, string? b)
    {
        return !string.IsNullOrWhiteSpace(a)
            && !string.IsNullOrWhiteSpace(b)
            && (a.Contains(b, StringComparison.OrdinalIgnoreCase)
                || b.Contains(a, StringComparison.OrdinalIgnoreCase));
    }

    private static string CoverUrl(string isbn)
    {
        return "https://www.bibdsl.co.uk/xmla/image-service.asp?ISBN=" + Uri.EscapeDataString(isbn)
            + "&SIZE=m&DBM=dd7692naier982134znxc72n10c7dncicvhdbfcjv73ncuv7dn22cmbnbfhfjds"
            + "&ERR=blank.gif&SSL=true";
    }
}
