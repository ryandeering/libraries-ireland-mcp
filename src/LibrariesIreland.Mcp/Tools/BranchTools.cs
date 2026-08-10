namespace LibrariesIreland.Mcp.Tools;

internal sealed class BranchTools(BranchDirectory directory, HomeLibraryStore home)
{
    // Detail costs one request per branch, so it is only fetched for a narrow result.
    private const int DetailThreshold = 3;
    private const int MaxBranchesReturned = 40;

    [Description(
        "Get the library service and branch the user has told us they use. Call this first when a " +
        "request depends on 'my library'. If it reports IsConfigured=false, ask the user which " +
        "library service and branch they use, then call set_home_library.")]
    public HomeLibrary GetHomeLibrary()
    {
        var current = home.Current;
        string note;

        if (!home.IsConfigured)
        {
            note = "Not configured. Ask the user which library service they belong to, and ideally "
                   + "which branch they use, then call set_home_library. Do not guess.";
        }
        else if (current.BranchName is { Length: > 0 })
        {
            note = "Home library is set. Searches default to this service, and availability is "
                   + "reported against this branch.";
        }
        else
        {
            note = "Library service is set but no usual branch. Set one with set_home_library to "
                   + "get answers about what is on the shelf at your own branch.";
        }

        return new HomeLibrary(
            current.Authority,
            Authorities.NameOf(current.Authority),
            current.BranchIrn,
            current.BranchName,
            home.IsConfigured,
            note,
            home.IsConfigured ? null : [.. Authorities.Descriptions]);
    }

    [Description(
        "Remember which library service, and optionally which branch, the user uses. Persists across " +
        "sessions. Only call this with a library the user has actually named. Never guess on their " +
        "behalf. The branch name is matched against the real branch list, so 'Ballyfermot' is enough.")]
    public async Task<HomeLibrary> SetHomeLibraryAsync(
        [Description("Library service, such as 'Dublin City', 'Cork County', 'dlr' or 'Fingal'.")]
        string authority,
        [Description("Optional usual branch, such as 'Ballyfermot'.")]
        string? branch = null,
        CancellationToken ct = default)
    {
        var key = Authorities.Resolve(authority);
        if (key is null)
        {
            return new HomeLibrary(
                null, null, null, null, false,
                $"'{authority}' did not match exactly one Irish library service. It may be "
                + "unrecognised, or it may be ambiguous, since 'Cork' could be either Cork service "
                + "and 'Dublin' could be Dublin City, Dun Laoghaire-Rathdown, Fingal or South "
                + "Dublin. Ask the user which one they mean, then call again.",
                [.. Authorities.Descriptions]);
        }

        var serviceName = Authorities.NameOf(key)!;

        if (string.IsNullOrWhiteSpace(branch))
        {
            var outcome = home.Save(key, null, null);
            return new HomeLibrary(
                key, serviceName, null, null, true,
                Durability(outcome, $"Searches now default to {serviceName}."));
        }

        var matches = await directory.SearchAsync(key, branch, ct);

        if (matches.Count == 1)
        {
            var outcome = home.Save(key, matches[0].Irn, matches[0].Name);
            return new HomeLibrary(
                key, serviceName, matches[0].Irn, matches[0].Name, true,
                Durability(
                    outcome,
                    $"Searches now default to {serviceName}, with {matches[0].Name} as the usual "
                    + "branch."));
        }

        // Save the service regardless, so the useful half of the answer sticks while the caller
        // resolves the branch.
        var partial = home.Save(key, null, null);

        var note = matches.Count > 1
            ? $"Library service saved as {serviceName}, but '{branch}' matches {matches.Count} "
              + $"branches: {string.Join(", ", matches.Take(10).Select(b => b.Name))}. Ask the user "
              + "which one, then call set_home_library again."
            : $"Library service saved as {serviceName}, but no branch matching '{branch}' was "
              + "found. Use find_branch to list the branches.";

        return new HomeLibrary(key, serviceName, null, null, true, Durability(partial, note));
    }

    [Description(
        "Look up public library branches by name, with address, phone and opening hours. Searches " +
        "the user's own library service by default. Pass scope=Nationwide to search every branch in " +
        "Ireland. Use this to resolve a branch name before calling set_home_library.")]
    public async Task<BranchSearchResponse> FindBranchAsync(
        [Description("Branch name or part of one, such as 'Ballyfermot'. Omit to list them all.")]
        string? name = null,
        [Description("Which library service's branches to search.")]
        Scope scope = Scope.HomeLibrary,
        [Description("Include address, phone and opening hours. This costs one extra request per "
                     + "branch, so it only applies when three or fewer branches match.")]
        bool includeDetails = true,
        CancellationToken ct = default)
    {
        if (scope == Scope.HomeLibrary && !home.IsConfigured)
        {
            var everywhere = await directory.SearchAsync(null, name, ct);
            return new BranchSearchResponse(
                [.. everywhere.Take(MaxBranchesReturned)],
                everywhere.Count,
                "No home library is set, so this searched every branch in Ireland. Ask the user "
                + "which library service they use, then call set_home_library.");
        }

        var authority = scope == Scope.Nationwide ? null : home.Current.Authority;
        var matches = await directory.SearchAsync(authority, name, ct);

        if (matches.Count == 0)
        {
            return new BranchSearchResponse(
                [], 0,
                $"No branch matching '{name}'. Call again without a name to list them all, or use "
                + "scope=Nationwide.");
        }

        if (includeDetails && matches.Count <= DetailThreshold)
        {
            var detailed = new List<Branch>(matches.Count);
            foreach (var b in matches)
            {
                detailed.Add(await directory.DetailAsync(authority, b.Irn, ct));
            }

            return new BranchSearchResponse(detailed, detailed.Count);
        }

        var capped = matches.Take(MaxBranchesReturned).ToList();
        string? note = null;

        if (matches.Count > capped.Count)
        {
            note = $"{matches.Count} branches matched, showing the first {capped.Count}. Narrow the "
                   + "name to get addresses and opening hours.";
        }
        else if (matches.Count > DetailThreshold)
        {
            note = $"Narrow the search to {DetailThreshold} or fewer branches to get addresses and "
                   + "opening hours.";
        }

        return new BranchSearchResponse(capped, matches.Count, note);
    }

    private static string Durability(SaveOutcome outcome, string detail)
    {
        return outcome.Persisted
            ? $"Saved. {detail}"
            : $"Set for this session only, because {outcome.Problem} when saving the config file. "
              + $"{detail} It will be gone after a restart. The server log has the detail.";
    }
}
