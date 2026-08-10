namespace LibrariesIreland.Mcp.Tests;

internal sealed class FakeCatalogue : HttpMessageHandler
{
    private readonly List<(string Fragment, string? Body, Exception? Error)> _matchers = [];

    public List<Uri> Requests { get; } = [];

    public string? LastQuery =>
        Requests.Count == 0 ? null : QueryValue(Requests[^1], "QRY");

    public FakeCatalogue When(string urlContains, string body)
    {
        _matchers.Add((urlContains, body, null));
        return this;
    }

    public FakeCatalogue Fails(string urlContains, Exception error)
    {
        _matchers.Add((urlContains, null, error));
        return this;
    }

    public static string QueryValue(Uri uri, string key)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&'))
        {
            var split = pair.IndexOf('=');
            if (split > 0 && pair[..split].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(split + 1)..]);
            }
        }

        return string.Empty;
    }

    // A feed carrying one record with the given holdings lines, in the catalogue's own format.
    public static string Feed(string title, string brn, params string[] holdings)
    {
        var lines = string.Join("&lt;br /&gt;", holdings);
        return $"""
            <?xml version="1.0"?>
            <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/">
            <channel><title>Spydus Search Results</title>
            <item>
            <title>{title}</title>
            <link>https://x/cgi-bin/spydus.exe/ENQ/WPAC/BIBENQ?SETLVL=&amp;BRN={brn}&amp;CF=BIB</link>
            <description>A blurb.</description>
            <content:encoded>&lt;b&gt;Author: &lt;/b&gt;Someone&lt;br /&gt;&lt;br /&gt;{lines}</content:encoded>
            </item>
            </channel></rss>
            """;
    }

    // What the catalogue sends for a widely held title: a count instead of per-copy lines.
    public static string SummarisedFeed(string title, string brn, int copies, int reserves)
    {
        var encoded = "&lt;b&gt;Author: &lt;/b&gt;Someone&lt;br /&gt;"
            + $"{reserves} reserves&lt;br /&gt;{copies} copies";

        return $"""
        <?xml version="1.0"?>
        <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/">
        <channel><title>Spydus Search Results</title>
        <item>
        <title>{title}</title>
        <link>https://x/cgi-bin/spydus.exe/ENQ/WPAC/BIBENQ?SETLVL=&amp;BRN={brn}&amp;CF=BIB</link>
        <description>A blurb.</description>
        <content:encoded>{encoded}</content:encoded>
        </item>
        </channel></rss>
        """;
    }

    public static string EmptyFeed() =>
        """
        <?xml version="1.0"?>
        <rss version="2.0" xmlns:content="http://purl.org/rss/1.0/modules/content/">
        <channel><title>Spydus Search Results</title></channel></rss>
        """;

    public static string ResultsPage(int total) =>
        $"""<main data-displaytype="LIST" data-settotal="{total}">rows</main>""";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(request.RequestUri!);

        // Answers and failures share one ordered list, so first-registered-wins holds across both.
        // A request for the RSS feed and a request for the count both contain "BIBENQ", and a test
        // needs to be able to answer one and fail the other.
        foreach (var (fragment, body, error) in _matchers)
        {
            if (!url.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (error is not null)
            {
                return Task.FromException<HttpResponseMessage>(error);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body!, Encoding.UTF8, "text/html"),
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(string.Empty),
        });
    }
}
