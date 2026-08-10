namespace LibrariesIreland.Mcp.Tests;

internal static class Fixtures
{
    public static TheoryData<string> LiveAuthorityNames()
    {
        var data = new TheoryData<string>();
        foreach (var line in File.ReadAllLines(Path.Combine("Fixtures", "authority-names.txt")))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                data.Add(line.Trim());
            }
        }

        return data;
    }
}
