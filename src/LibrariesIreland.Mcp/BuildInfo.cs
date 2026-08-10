namespace LibrariesIreland.Mcp;

internal static class BuildInfo
{
    public static string Version { get; } = Read();

    public static string UserAgent { get; } =
        $"libraries-ireland-mcp/{Version} (personal library lookup tool)";

    private static string Read()
    {
        var informational = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(BuildInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
