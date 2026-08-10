namespace LibrariesIreland.Mcp.Spydus;

// The site's robots.txt asks robots not to trawl its database.
internal sealed class SpydusClient : IDisposable
{
    private static readonly TimeSpan DefaultMinimumGap = TimeSpan.FromMilliseconds(1100);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReferenceTtl = TimeSpan.FromDays(14);
    private const int MaxCacheEntries = 200;

    private readonly HttpClient _http;
    private readonly TimeSpan _minimumGap;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (DateTimeOffset Expires, string Body)> _cache = [];
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public SpydusClient(HttpMessageHandler? handler = null, TimeSpan? minimumGap = null)
    {
        _minimumGap = minimumGap ?? DefaultMinimumGap;

        // A shared cookie container pins the load balancer node, which is what keeps relevance
        // sorting stable: it degrades to title order on some backends.
        handler ??= new SocketsHttpHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(BuildInfo.UserAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml");
    }

    public Task<string> SearchRssAsync(string host, string qry, int limit, CancellationToken ct)
    {
        var url = BuildSearchUrl(host, qry, limit, rss: true);
        return GetAsync(url, CacheTtl, ct);
    }

    // The HTML body is fetched only for the total match count, which RSS does not carry.
    public Task<string> SearchHtmlAsync(string host, string qry, CancellationToken ct)
    {
        var url = BuildSearchUrl(host, qry, 1, rss: false);
        return GetAsync(url, CacheTtl, ct);
    }

    public Task<string> RecordPageAsync(string host, string brn, CancellationToken ct)
    {
        var url = RecordUrl(host, brn);
        return GetAsync(url, CacheTtl, ct);
    }

    public Task<string> AdvancedSearchPageAsync(string host, CancellationToken ct)
    {
        var url = $"https://{host}/cgi-bin/spydus.exe/MSGTRN/WPAC/COMB";
        return GetAsync(url, ReferenceTtl, ct);
    }

    public Task<string> BranchDetailAsync(string host, string irn, CancellationToken ct)
    {
        var query = Query(("QRY", $"IRN({irn})"), ("QRYTEXT", "branch"));
        return GetAsync(
            $"https://{host}/cgi-bin/spydus.exe/ENQ/WPAC/GENENQ?{query}", ReferenceTtl, ct);
    }

    // No SORTS: the catalogue ignores it on a stateless query, and on the occasions it does not,
    // it buries the relevant titles. Its default ordering is the good one.
    public static string BuildSearchUrl(string host, string qry, int limit, bool rss)
    {
        var sb = new StringBuilder($"https://{host}/cgi-bin/spydus.exe/ENQ/WPAC/BIBENQ?");
        sb.Append(Query(
            ("QRY", qry),
            ("QRYTEXT", "libraries-ireland-mcp"),
            ("SETLVL", "SET"),
            ("MODE", "GLB"),
            ("CF", "BIB"),
            ("NRECS", limit.ToString())));

        if (rss)
        {
            sb.Append("&FMT=RSS&XSLT=rss.xsl");
        }

        return sb.ToString();
    }

    public static string RecordUrl(string host, string brn)
    {
        const string path = "/cgi-bin/spydus.exe/ENQ/WPAC/BIBENQ";
        return $"https://{host}{path}?SETLVL=&BRN={brn}&CF=BIB";
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }

    private async Task<string> GetAsync(string url, TimeSpan ttl, CancellationToken ct)
    {
        if (TryGetCached(url, out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            // Re-check: another caller may have fetched this while we waited for the gate.
            if (TryGetCached(url, out var raced))
            {
                return raced;
            }

            var since = DateTimeOffset.UtcNow - _lastRequest;
            if (since < _minimumGap)
            {
                await Task.Delay(_minimumGap - since, ct);
            }

            using var response = await _http.GetAsync(url, ct);
            _lastRequest = DateTimeOffset.UtcNow;
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);

            lock (_cache)
            {
                if (_cache.Count > MaxCacheEntries)
                {
                    Evict();
                }

                _cache[url] = (DateTimeOffset.UtcNow.Add(ttl), body);
            }

            return body;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Evict()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _cache.Where(e => e.Value.Expires <= now).Select(e => e.Key).ToList();

        foreach (var key in expired)
        {
            _cache.Remove(key);
        }

        if (_cache.Count > MaxCacheEntries)
        {
            _cache.Clear();
        }
    }

    private bool TryGetCached(string url, out string body)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(url, out var hit) && hit.Expires > DateTimeOffset.UtcNow)
            {
                body = hit.Body;
                return true;
            }
        }

        body = string.Empty;
        return false;
    }

    private static string Query(params ReadOnlySpan<(string Key, string Value)> pairs)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in pairs)
        {
            if (sb.Length > 0)
            {
                sb.Append('&');
            }

            sb.Append(key).Append('=').Append(Uri.EscapeDataString(value));
        }

        return sb.ToString();
    }
}
