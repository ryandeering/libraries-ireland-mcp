namespace LibrariesIreland.Mcp.Spydus;

internal sealed class BranchDirectory(SpydusClient client) : IDisposable
{
    private readonly Dictionary<string, IReadOnlyList<Branch>> _byHost = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    public async Task<IReadOnlyList<Branch>> ForAuthorityAsync(string? authority, CancellationToken ct)
    {
        var host = Authorities.HostFor(authority);

        await _gate.WaitAsync(ct);
        try
        {
            if (_byHost.TryGetValue(host, out var cached))
            {
                return cached;
            }

            var html = await client.AdvancedSearchPageAsync(host, ct);
            var branches = HtmlScrape.BranchList(html);

            var authorityName = Authorities.NameOf(authority);
            var resolved = branches
                .Select(b => b with { Authority = authorityName ?? GuessAuthority(b.Name) })
                .ToList();

            _byHost[host] = resolved;
            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<Branch>> SearchAsync(string? authority, string? text, CancellationToken ct)
    {
        var all = await ForAuthorityAsync(authority, ct);
        if (string.IsNullOrWhiteSpace(text))
        {
            return all;
        }

        var needle = text.Trim();

        var exact = all.Where(b => b.Name.Equals(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count > 0)
        {
            return exact;
        }

        var contains = all.Where(b => b.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        if (contains.Count > 0)
        {
            return contains;
        }

        var words = needle.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .ToArray();

        return words.Length == 0
            ? []
            : [.. all.Where(b => words.Any(w => b.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))];
    }

    public async Task<Branch> DetailAsync(string? authority, string irn, CancellationToken ct)
    {
        var all = await ForAuthorityAsync(authority, ct);
        var seed = all.FirstOrDefault(b => b.Irn == irn) ?? new Branch(irn, $"Branch {irn}");
        var html = await client.BranchDetailAsync(Authorities.HostFor(authority), irn, ct);
        return HtmlScrape.BranchDetail(html, seed);
    }

    private static string? GuessAuthority(string branchName)
    {
        string? best = null;
        var bestLen = 0;

        foreach (var key in Authorities.Keys)
        {
            var name = Authorities.NameOf(key)!;
            var head = name.Split(" Librar")[0].Split(" County")[0].Trim();
            if (head.Length > bestLen && branchName.StartsWith(head, StringComparison.OrdinalIgnoreCase))
            {
                best = name;
                bestLen = head.Length;
            }
        }

        return best;
    }
}
