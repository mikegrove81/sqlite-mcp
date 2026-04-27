# SQLite MCP — Setup Guide

Connect Claude to your SQLite databases. Designed for Plex and Emby media databases, but works with any SQLite file.

---

## Prerequisites

- .NET 8.0 SDK — [download](https://dot.net)
- A SQLite database file (`.db`, `.sqlite`, `.db3`)
- Claude Code or Claude Desktop

---

## Step 1 — Clone the Repo

```bash
git clone https://github.com/mikegrove81/sqlite-mcp C:/your-path/sqlite-mcp
cd C:/your-path/sqlite-mcp
```

---

## Step 2 — Configure Your Databases

Copy the example config:

**Windows:**
```
copy SqliteMcp\appsettings.example.json SqliteMcp\appsettings.json
```

**Linux/Mac:**
```bash
cp SqliteMcp/appsettings.example.json SqliteMcp/appsettings.json
```

Open `SqliteMcp/appsettings.json` and set your database paths:

```json
{
  "Databases": {
    "plex": "C:\\Users\\YourName\\AppData\\Local\\Plex Media Server\\Plug-in Support\\Databases\\com.plexapp.plugins.library.db"
  },
  "DefaultDatabase": "plex"
}
```

**Multiple databases:** Add as many as you want:

```json
{
  "Databases": {
    "plex": "C:\\path\\to\\plex.db",
    "emby": "C:\\path\\to\\emby.db",
    "myapp": "C:\\path\\to\\myapp.db"
  },
  "DefaultDatabase": "plex"
}
```

`appsettings.json` is gitignored — your paths will never be committed.

---

## Step 3 — Build

```bash
dotnet build --configuration Release
```

Test that it runs:

```bash
dotnet run --project SqliteMcp --configuration Release
```

You should see it waiting for MCP connections. Ctrl+C to exit.

---

## Step 4 — Register with Claude Code

```bash
claude mcp add sqlite dotnet -- run --project "C:/your-path/sqlite-mcp/SqliteMcp" --configuration Release
```

Restart Claude Code. Verify with:

```bash
claude mcp list
```

You should see `sqlite` in the list.

---

## Step 5 — Register with Claude Desktop (Optional)

Claude Desktop config file:

- **Windows:** `C:\Users\YourName\AppData\Roaming\Claude\claude_desktop_config.json`
- **Mac:** `~/Library/Application Support/Claude/claude_desktop_config.json`

Add to the `mcpServers` block:

```json
{
  "mcpServers": {
    "sqlite": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/your-path/sqlite-mcp/SqliteMcp", "--configuration", "Release"]
    }
  }
}
```

Restart Claude Desktop after saving.

---

## Step 6 — Test It

In Claude Code:

> "List the tables in my Plex database"

or

> "Show me my Plex databases"

If it returns data, you're connected.

---

## Finding Your Plex Database

| Platform | Path |
|----------|------|
| Windows | `C:\Users\YourName\AppData\Local\Plex Media Server\Plug-in Support\Databases\com.plexapp.plugins.library.db` |
| Linux | `/var/lib/plexmediaserver/Library/Application Support/Plex Media Server/Plug-in Support/Databases/com.plexapp.plugins.library.db` |
| Mac | `~/Library/Application Support/Plex Media Server/Plug-in Support/Databases/com.plexapp.plugins.library.db` |

> **Note:** Close Plex before running queries that modify data. The database may be locked while Plex is running. Read-only queries are safe.

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Tools not showing up | Restart Claude Code / Claude Desktop after registration |
| "Database not found" | Check the path in appsettings.json — use full absolute path, double backslashes on Windows |
| "Database is locked" | Close Plex/Emby before running write queries |
| "dotnet not found" | Install .NET 8.0 SDK and ensure it's on your PATH |
| CSV import can't find file | Use the full path, or put the file in Downloads or temp |
