# libraries-ireland-mcp

An MCP server for the [Libraries Ireland](https://librariesireland.spydus.ie) catalogue, which
covers every public library service in Ireland. It finds books, reports which branches have them on
the shelf, and links to the catalogue page where you reserve them.

> Find me a decent book on the Spanish Civil War I can get locally.

> I liked Kafka on the Shore. What else along those lines can I borrow?

> Where can I get Misha Glenny's The Fall of Yugoslavia?

> Any Irish-language picture books for children in my service?

> When is Ballyfermot open on Saturdays?

## Background

I wrote this mostly as an exercise in building an MCP server. The Libraries Ireland search is
awkward to use, so I let a model work through the results for me instead. It's good at narrowing a
topic or author down to what I can borrow.

## Installing

macOS and Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/ryandeering/libraries-ireland-mcp/main/install.sh | sh
```

Windows, in PowerShell:

```powershell
irm https://raw.githubusercontent.com/ryandeering/libraries-ireland-mcp/main/install.ps1 | iex
```

Register it with your client:

```bash
# Claude Code
claude mcp add libraries-ireland ~/.local/bin/libraries-ireland-mcp

# Codex
codex mcp add libraries-ireland -- ~/.local/bin/libraries-ireland-mcp
```

On Windows, use the full path to the .exe instead.

For clients configured with JSON:

```json
{
  "mcpServers": {
    "libraries-ireland": {
      "command": "/absolute/path/to/libraries-ireland-mcp"
    }
  }
}
```

On Windows, escape the backslashes in the path.

## First run

Tell it which library you use:

> I'm with Dublin City libraries, I usually go to Ballyfermot.

It saves this to $XDG_CONFIG_HOME/libraries-ireland-mcp/config.json if that variable is set,
otherwise ~/.config/libraries-ireland-mcp/config.json on macOS and Linux or
%APPDATA%\libraries-ireland-mcp\config.json on Windows. That file is the only thing it writes. Until
it's set, anything scoped to your library asks you first.

## Tools

| Tool | Purpose |
|---|---|
| browse_subject | Books on a topic held by your service. Use it for "a good book about X". |
| search_catalogue | Search by title, author, subject, series or ISBN, filtered by language, format, fiction or non-fiction, readership, year and availability. |
| get_book | The full record, with the ISBN and every copy in every branch. |
| where_can_i_get_this | Whether a title is at your branch, elsewhere in your service, or only in another county, where it can be requested to your branch. |
| find_branch | A branch's address, phone number and opening hours. |
| get_home_library, set_home_library | Read or set your library. |

## Limits

It doesn't place reservations. It gives you the catalogue link to finish in a browser.

It doesn't judge whether a book is any good. Ask your model to look up reviews if you like, but take
what it already thinks it knows about a title with a pinch of salt.

## Rate limiting

This is a free, non-commercial project. It doesn't resell, republish or mirror the catalogue's
data. The site's robots.txt asks robots not to trawl its database, and
[SpydusClient](src/LibrariesIreland.Mcp/Spydus/SpydusClient.cs) enforces the following:

- one request at a time, at least 1.1 seconds apart
- an in-process response cache, with branch data cached for a fortnight
- a user agent that identifies the tool
- hard result caps, with no pagination crawling or prefetching
- read-only access, so nothing is written to the catalogue or to any library account

**Please keep this in place if you fork it.**

## Internals

There's no public API. The catalogue runs on Spydus, which returns structured XML. Filters compile
into one stateless query using + for AND, / for OR and - for NOT.

Searches scoped to your service go through that service's subdomain. The federated host filters
which titles match but still lists every copy, so a Cork copy would show up as if it were in your
branch.

## Building from source

```bash
dotnet publish src/LibrariesIreland.Mcp -c Release -r osx-arm64 -o ./dist
```

Swap in your platform's runtime identifier: osx-arm64, osx-x64, linux-x64, linux-arm64 or win-x64.
Native AOT doesn't cross-compile, so build on the platform you'll run it on.

You need the .NET 11 SDK, at the version pinned in global.json, and a C toolchain for the AOT linker:
the Xcode command line tools on macOS (xcode-select --install), clang and zlib1g-dev on Linux, or
the "Desktop development with C++" workload from the Visual Studio Build Tools on Windows.

## Licence

MIT. Not affiliated with Libraries Ireland, any local authority, or Civica.

There's nothing to buy, no hosted service, no accounts and no analytics. Nothing leaves your
machine except the catalogue searches you ask for.
