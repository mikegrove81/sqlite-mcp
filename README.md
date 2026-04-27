# SQLite MCP

Gives Claude direct access to SQLite databases. Query data, explore schemas, run statements, import CSV files — all through natural language.

Designed primarily for querying **Plex** and **Emby** media databases, but works with any SQLite database.

Works with **Claude Code** and **Claude Desktop**.

## What You Can Ask Claude

- "List the tables in my Plex database"
- "Show me all movies in my Plex library"
- "How many episodes do I have per show?"
- "Find all columns with 'title' in the name"
- "What's the schema of the media_items table?"
- "Import ratings.csv into my database"

## Setup

See [docs/setup-guide.md](docs/setup-guide.md) for full installation instructions.

**Quick version:**
1. Clone this repo
2. Copy `SqliteMcp/appsettings.example.json` → `SqliteMcp/appsettings.json` and add your database paths
3. `dotnet build --configuration Release`
4. `claude mcp add sqlite dotnet -- run --project "path/to/SqliteMcp" --configuration Release`
5. Restart Claude Code

## Plex Database Location

| Platform | Path |
|----------|------|
| Windows | `C:\Users\YourName\AppData\Local\Plex Media Server\Plug-in Support\Databases\com.plexapp.plugins.library.db` |
| Linux | `/var/lib/plexmediaserver/Library/Application Support/Plex Media Server/Plug-in Support/Databases/com.plexapp.plugins.library.db` |
| Mac | `~/Library/Application Support/Plex Media Server/Plug-in Support/Databases/com.plexapp.plugins.library.db` |

## Tools

| Tool | What It Does |
|------|-------------|
| `sqlite_list_databases` | List configured databases with file sizes |
| `sqlite_list_tables` | List tables and views |
| `sqlite_describe_table` | Show column metadata |
| `sqlite_search_columns` | Find columns by name across all tables |
| `sqlite_script_table` | Return the CREATE TABLE statement |
| `sqlite_db_info` | Database file info, page count, journal mode |
| `sqlite_query` | Run SELECT queries — markdown output |
| `sqlite_query_json` | Run SELECT queries — JSON output |
| `sqlite_execute` | Run INSERT, UPDATE, DELETE |
| `sqlite_execute_ddl` | Run CREATE, ALTER, DROP |
| `sqlite_import_csv` | Import CSV — auto-creates and type-optimizes table |

## Requirements

- .NET 8.0 SDK
- Any SQLite database file (.db, .sqlite, .db3)
