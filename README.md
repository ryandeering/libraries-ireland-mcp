# libraries-ireland-mcp

An MCP server for the [Libraries Ireland](https://librariesireland.spydus.ie) catalogue, which
covers every public library service in the country.

Ask it for something like "a decent book on the history of the Balkans I can get from my local
library" and you get back titles with their current shelf availability and a link to reserve them.

Some example queries:

> Find me a decent book on the Spanish Civil War I can get locally.

> I liked Kafka on the Shore. What else along those lines can I borrow?

> Something on electronics that is on the shelf right now at my branch.

> Where can I get Misha Glenny's The Fall of Yugoslavia?

> Has my library got anything by Stephen Graham Jones, and is any of it in right now?

> Any Irish-language picture books for children in my service?
>
> Any recently released sci-fi audiobooks in my local branch?

> When is Ballyfermot open on Saturdays?
>
> Which Dublin City branches are near Inchicore?

## Why?

This was mostly just an exercise in writing an MCP server. I know MCPs aren't as cool and trendy as
they were last year but I thought it'd be fun to look into it anyway, since it seems to have matured
as a protocol somewhat. I wanted to have something to act as a wrapper for the obtuse Libraries
Ireland search engine and catalogue. I've grappled with it over the years and recently had this idea
to let a frontier model pore over the results for me.

I also thought it'd be handy to break down finding a book on a specific topic as simple as possible
too, it's been nice to use it with certain topics and authors I'm interested in, without having to
collate or account for results that aren't relevant to my search.

## Installing

macOS and Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/ryandeering/libraries-ireland-mcp/main/install.sh | sh
```

Windows, in PowerShell:

```powershell
irm https://raw.githubusercontent.com/ryandeering/libraries-ireland-mcp/main/install.ps1 | iex
```

Then register it with your MCP client. Claude Code:

```bash
claude mcp add libraries-ireland ~/.local/bin/libraries-ireland-mcp
```

Codex:

```bash
codex mcp add libraries-ireland -- ~/.local/bin/libraries-ireland-mcp
```

Run `codex mcp list` to confirm the server is configured, then start a new Codex session. On
Windows, use the full path to the `.exe` in place of `~/.local/bin/libraries-ireland-mcp` in either
command.

For any client that takes JSON configuration:

```json
{
  "mcpServers": {
    "libraries-ireland": {
      "command": "/absolute/path/to/libraries-ireland-mcp"
    }
  }
}
```

On Windows, use a Windows path with escaped backslashes, for example
`"C:\\Users\\you\\bin\\libraries-ireland-mcp.exe"`.

## First run

Tell it which library you use, in conversation:

> I'm with Dublin City libraries, I usually go to Ballyfermot.

That calls `set_home_library`, which resolves the branch against the live branch list and saves it,
so it survives restarts. Anything scoped to "my library" uses it from then on.

That config file is the only thing the server writes to disk. It lives at
`$XDG_CONFIG_HOME/libraries-ireland-mcp/config.json` when that variable is set, otherwise
`~/.config/libraries-ireland-mcp/config.json` on macOS and Linux, and
`%APPDATA%\libraries-ireland-mcp\config.json` on Windows.

Until you set it, the tools will say so and ask you.


## Tools

| Tool | What it does |
|---|---|
| `browse_subject` | Books on a topic that your service holds. Use it for "a good book about X". |
| `search_catalogue` | Full search: title, author, subject, series and ISBN, with filters for language, format, fiction or non-fiction, readership, publication years and current availability. |
| `get_book` | A full record, including the ISBN and every copy in every branch. |
| `where_can_i_get_this` | Whether a title is on your shelf, elsewhere in your service, or only in another county. If it is held elsewhere, it will tell you that it can be requested and sent to your local branch. |
| `find_branch` | Branches by name, with address, phone number and opening hours. |
| `get_home_library`, `set_home_library` | Read and set the library you use. |

## Omissions

It will not place reservations. It tells you whether a title can be reserved and gives you the
catalogue link to finish the job in a browser.

It will not tell you which book is any good. You're welcome to ask your model to go looking for
Goodreads reviews or the general reputation of a book, but I'd take whatever it already thinks it
knows about a given title with a pinch of salt.

## Courtesy towards the catalogue

This is a free, non-commercial project. I make nothing from it, and it does not resell, republish or
mirror the catalogue's data anywhere. The site's `robots.txt` asks robots not to trawl its database,
so this tool only answers questions a person has actually asked.

- every request serialised behind a global lock, with a minimum gap of 1.1 seconds
- responses cached in process, and branch reference data cached for a fortnight
- a user agent that identifies the tool
- hard result caps, with no pagination crawling and no prefetching
- read-only throughout: it writes nothing to the catalogue and nothing to any library account

**Please leave that in place if you fork it.**

## How it works

There is no public API. The catalogue runs on Spydus, which returns structured XML.

Filters compose into a single stateless query expression, using `+` for AND, `/` for OR and `-` for
NOT. Scoping to your own service means going through that service's subdomain, because the federated
host filters which titles match without filtering which copies are listed, and would otherwise
report a Cork copy as though it were sitting in your local branch.


## Building from source

```bash
dotnet publish src/LibrariesIreland.Mcp -c Release -r osx-arm64 -o ./dist
```

Substitute your platform for the runtime identifier: `osx-arm64`, `osx-x64`, `linux-x64`,
`linux-arm64` or `win-x64`. Nothing in the project is tied to a particular operating system, though
Native AOT does not cross-compile, so build on the platform you intend to run on.

Building needs the .NET 10 SDK. AOT hands off to the system linker, so it also needs a C toolchain:
on macOS the Xcode command line tools (`xcode-select --install`), on Linux `clang` and
`zlib1g-dev`, and on Windows the "Desktop development with C++" workload from the Visual Studio
Build Tools.

## Licence

MIT. Not affiliated with Libraries Ireland, any local authority, or Civica.

Free and non-commercial. There is nothing to buy, no hosted service behind it, no accounts, no
analytics and no data leaving your machine other than the catalogue searches you ask for. 
