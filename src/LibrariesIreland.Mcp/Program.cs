namespace LibrariesIreland.Mcp;

internal static class Program
{
    private const string Instructions =
        "Read-only access to the Libraries Ireland public library catalogue, covering every public " +
        "library service in Ireland.\n\n" +
        "This server finds books and reports where they can be borrowed. It cannot place " +
        "reservations, because the library website requires a card login and a CAPTCHA. Finish by " +
        "giving the user the catalogue link and telling them what they will find there.\n\n" +
        "It also does not rate books. When asked for 'a good book about X', use browse_subject to get " +
        "the candidates that are actually borrowable, judge their quality yourself (searching the web " +
        "if that helps), and then recommend from what is genuinely available.\n\n" +
        "Anything scoped to 'my library' depends on get_home_library. If it is not configured, ask the " +
        "user which library service they belong to rather than guessing.";

    private static async Task<int> Main()
    {
        // Under NativeAOT there is no reflection-based serialization, so every DTO that crosses the
        // tool boundary must resolve through the source-generated context.
        var json = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        json.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        json.MakeReadOnly();

        using var client = new SpydusClient();
        var home = new HomeLibraryStore();
        using var directory = new BranchDirectory(client);
        var catalogue = new CatalogueTools(client, home);
        var branches = new BranchTools(directory, home);

        McpServerTool Tool(Delegate fn, string name, bool readOnly = true) =>
            McpServerTool.Create(fn, new McpServerToolCreateOptions
            {
                Name = name,
                ReadOnly = readOnly,
                Destructive = false,
                Idempotent = true,
                OpenWorld = true,
                UseStructuredContent = true,
                SerializerOptions = json,
            });

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "libraries-ireland", Version = BuildInfo.Version },
            ServerInstructions = Instructions,
            ToolCollection =
            [
                Tool(catalogue.SearchCatalogueAsync, "search_catalogue"),
                Tool(catalogue.BrowseSubjectAsync, "browse_subject"),
                Tool(catalogue.GetBookAsync, "get_book"),
                Tool(catalogue.WhereCanIGetThisAsync, "where_can_i_get_this"),
                Tool(branches.FindBranchAsync, "find_branch"),
                Tool(branches.GetHomeLibrary, "get_home_library"),
                Tool(branches.SetHomeLibraryAsync, "set_home_library", readOnly: false),
            ],
        };

        await using var transport = new StdioServerTransport(options);
        await using var server = McpServer.Create(transport, options);
        await server.RunAsync();
        return 0;
    }
}
