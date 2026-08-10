namespace LibrariesIreland.Mcp.Config;

public sealed record SaveOutcome(HomeLibraryConfig Config, bool Persisted, string? Problem);

public sealed record HomeLibraryConfig(
    string? Authority = null,
    string? BranchIrn = null,
    string? BranchName = null);

// The server's only durable state, written only in response to an explicit set_home_library call.
// Nothing scoped ever guesses a default: when this is unset, the tools say so and ask the user.
internal sealed class HomeLibraryStore
{
    private readonly string _path;
    private HomeLibraryConfig _current;

    public HomeLibraryStore()
    {
        _path = Path.Combine(ConfigRoot(), "libraries-ireland-mcp", "config.json");
        _current = Load() ?? SeedFromEnvironment();
    }

    public HomeLibraryConfig Current => _current;

    public bool IsConfigured => Authorities.IsKnown(_current.Authority);

    public string Host => Authorities.HostFor(_current.Authority);

    public string Description
    {
        get
        {
            if (!IsConfigured)
            {
                return "not set";
            }

            var name = Authorities.NameOf(_current.Authority)!;
            return _current.BranchName is { Length: > 0 } branch
                ? $"{name} (preferred branch: {branch})"
                : name;
        }
    }

    public SaveOutcome Save(string? authority, string? branchIrn, string? branchName)
    {
        _current = new HomeLibraryConfig(authority, branchIrn, branchName);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(
                _path,
                JsonSerializer.Serialize(_current, AppJsonSerializerContext.Default.HomeLibraryConfig));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"libraries-ireland-mcp: could not write {_path}: {ex.Message}");

            var reason = ex is UnauthorizedAccessException
                ? "permission was refused"
                : "the file could not be written";

            return new SaveOutcome(_current, Persisted: false, reason);
        }

        return new SaveOutcome(_current, Persisted: true, null);
    }

    private HomeLibraryConfig? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return JsonSerializer.Deserialize(
                File.ReadAllText(_path), AppJsonSerializerContext.Default.HomeLibraryConfig);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string ConfigRoot()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return xdg;
        }

        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                return appData;
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
    }

    private static HomeLibraryConfig SeedFromEnvironment()
    {
        var authority = Authorities.Resolve(
            Environment.GetEnvironmentVariable("LIBRARIES_IE_AUTHORITY"));
        var branch = Environment.GetEnvironmentVariable("LIBRARIES_IE_BRANCH");

        return new HomeLibraryConfig(authority, string.IsNullOrWhiteSpace(branch) ? null : branch);
    }
}
