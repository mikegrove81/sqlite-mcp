using System.ComponentModel;
using System.Text;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;
using SqliteMcp.Services;

namespace SqliteMcp.Tools;

[McpServerToolType]
public class SchemaTools
{
    private readonly SqliteConnectionFactory _db;

    public SchemaTools(SqliteConnectionFactory db) => _db = db;

    [McpServerTool(Name = "sqlite_list_databases", ReadOnly = true)]
    [Description("List all configured SQLite databases with their file paths and sizes")]
    public Task<string> ListDatabases(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Configured SQLite Databases");
        sb.AppendLine();
        sb.AppendLine("| Name | Path | Size |");
        sb.AppendLine("|------|------|------|");

        foreach (var db in _db.AvailableDatabases)
        {
            var path = GetPath(db);
            var size = path != null && File.Exists(path)
                ? $"{new FileInfo(path).Length / 1024.0 / 1024.0:N1} MB"
                : "(not found)";
            sb.AppendLine($"| {db}{(db == _db.DefaultDatabase ? " *(default)*" : "")} | {path ?? "unknown"} | {size} |");
        }

        return Task.FromResult(sb.ToString());
    }

    [McpServerTool(Name = "sqlite_list_tables", ReadOnly = true)]
    [Description("List tables and views in a SQLite database, optionally filtered by name pattern")]
    public async Task<string> ListTables(
        [Description("Database name as configured in appsettings.json (e.g. 'plex'). Defaults to the configured default.")] string? database = null,
        [Description("Table name LIKE pattern (e.g. '%media%')")] string? filter = null,
        CancellationToken ct = default)
    {
        var sql = "SELECT name, type FROM sqlite_master WHERE type IN ('table','view')";
        if (!string.IsNullOrEmpty(filter))
            sql += " AND name LIKE @filter";
        sql += " ORDER BY type, name";

        await using var conn = _db.Create(database);
        await conn.OpenAsync(ct);
        await using var cmd = new SqliteCommand(sql, conn);
        cmd.CommandTimeout = 30;
        if (!string.IsNullOrEmpty(filter))
            cmd.Parameters.AddWithValue("@filter", filter);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine($"## Tables and Views in [{database ?? _db.DefaultDatabase}]");
        sb.AppendLine();
        sb.AppendLine("| Name | Type |");
        sb.AppendLine("|------|------|");
        int count = 0;
        while (await rdr.ReadAsync(ct))
        {
            sb.AppendLine($"| {rdr["name"]} | {rdr["type"]} |");
            count++;
        }
        sb.AppendLine();
        sb.AppendLine($"*{count} objects found.*");
        return sb.ToString();
    }

    [McpServerTool(Name = "sqlite_describe_table", ReadOnly = true)]
    [Description("Show column metadata for a SQLite table including types, nullability, defaults, and primary keys")]
    public async Task<string> DescribeTable(
        [Description("Table name")] string table,
        [Description("Database name as configured in appsettings.json (e.g. 'plex'). Defaults to the configured default.")] string? database = null,
        CancellationToken ct = default)
    {
        await using var conn = _db.Create(database);
        await conn.OpenAsync(ct);

        await using var cmd = new SqliteCommand($"PRAGMA table_info([{table}])", conn);
        cmd.CommandTimeout = 30;
        await using var rdr = await cmd.ExecuteReaderAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine($"## [{database ?? _db.DefaultDatabase}].[{table}]");
        sb.AppendLine();
        sb.AppendLine("| # | Column | Type | Not Null | Default | PK |");
        sb.AppendLine("|---|--------|------|----------|---------|-----|");
        int count = 0;
        while (await rdr.ReadAsync(ct))
        {
            var def = rdr["dflt_value"] == DBNull.Value ? "" : rdr["dflt_value"]?.ToString();
            var notNull = Convert.ToInt32(rdr["notnull"]) == 1 ? "YES" : "";
            var pk = Convert.ToInt32(rdr["pk"]) > 0 ? "PK" : "";
            sb.AppendLine($"| {rdr["cid"]} | {rdr["name"]} | {rdr["type"]} | {notNull} | {def} | {pk} |");
            count++;
        }
        sb.AppendLine();
        sb.AppendLine($"*{count} columns.*");
        return sb.ToString();
    }

    [McpServerTool(Name = "sqlite_search_columns", ReadOnly = true)]
    [Description("Search for columns by name across all tables in a SQLite database")]
    public async Task<string> SearchColumns(
        [Description("Column name LIKE pattern (e.g. '%title%')")] string columnPattern,
        [Description("Database name as configured in appsettings.json (e.g. 'plex'). Defaults to the configured default.")] string? database = null,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT m.name AS table_name, p.name AS column_name, p.type AS data_type
            FROM sqlite_master m
            JOIN pragma_table_info(m.name) p
            WHERE m.type = 'table' AND p.name LIKE @pattern
            ORDER BY m.name, p.name
            """;

        await using var conn = _db.Create(database);
        await conn.OpenAsync(ct);
        await using var cmd = new SqliteCommand(sql, conn);
        cmd.CommandTimeout = 30;
        cmd.Parameters.AddWithValue("@pattern", columnPattern);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine($"## Columns matching '{columnPattern}' in [{database ?? _db.DefaultDatabase}]");
        sb.AppendLine();
        sb.AppendLine("| Table | Column | Type |");
        sb.AppendLine("|-------|--------|------|");
        int count = 0;
        while (await rdr.ReadAsync(ct))
        {
            sb.AppendLine($"| {rdr["table_name"]} | {rdr["column_name"]} | {rdr["data_type"]} |");
            count++;
        }
        sb.AppendLine();
        sb.AppendLine($"*{count} matches.*");
        return sb.ToString();
    }

    [McpServerTool(Name = "sqlite_script_table", ReadOnly = true)]
    [Description("Return the CREATE TABLE statement for an existing SQLite table")]
    public async Task<string> ScriptTable(
        [Description("Table name")] string table,
        [Description("Database name as configured in appsettings.json (e.g. 'plex'). Defaults to the configured default.")] string? database = null,
        CancellationToken ct = default)
    {
        const string sql = "SELECT sql FROM sqlite_master WHERE type='table' AND name=@table";

        await using var conn = _db.Create(database);
        await conn.OpenAsync(ct);
        await using var cmd = new SqliteCommand(sql, conn);
        cmd.CommandTimeout = 30;
        cmd.Parameters.AddWithValue("@table", table);
        var result = await cmd.ExecuteScalarAsync(ct);

        if (result == null || result == DBNull.Value)
            return $"Table '{table}' not found in [{database ?? _db.DefaultDatabase}].";

        return $"```sql\n{result};\n```";
    }

    [McpServerTool(Name = "sqlite_db_info", ReadOnly = true)]
    [Description("Show SQLite database file info: size, page count, journal mode, table count, and SQLite version")]
    public async Task<string> DbInfo(
        [Description("Database name as configured in appsettings.json (e.g. 'plex'). Defaults to the configured default.")] string? database = null,
        CancellationToken ct = default)
    {
        var dbName = database ?? _db.DefaultDatabase;
        var path = GetPath(dbName);
        var fileSize = path != null && File.Exists(path)
            ? $"{new FileInfo(path).Length / 1024.0 / 1024.0:N2} MB"
            : "(file not found)";

        await using var conn = _db.Create(database);
        await conn.OpenAsync(ct);

        async Task<string> Pragma(string name)
        {
            await using var c = new SqliteCommand($"PRAGMA {name}", conn) { CommandTimeout = 10 };
            var v = await c.ExecuteScalarAsync(ct);
            return v?.ToString() ?? "";
        }

        var pageCount = await Pragma("page_count");
        var pageSize  = await Pragma("page_size");
        var journalMode = await Pragma("journal_mode");
        var sqliteVer = await Pragma("user_version");

        await using var tcmd = new SqliteCommand(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table'", conn) { CommandTimeout = 10 };
        var tableCount = await tcmd.ExecuteScalarAsync(ct);

        await using var vcmd = new SqliteCommand("SELECT sqlite_version()", conn) { CommandTimeout = 10 };
        var version = await vcmd.ExecuteScalarAsync(ct);

        return $"""
            ## SQLite Database Info: [{dbName}]

            | Property | Value |
            |----------|-------|
            | File path | {path ?? "unknown"} |
            | File size | {fileSize} |
            | SQLite version | {version} |
            | Page count | {pageCount} |
            | Page size | {pageSize} bytes |
            | Journal mode | {journalMode} |
            | Table count | {tableCount} |
            | User version | {sqliteVer} |
            """;
    }

    private string? GetPath(string dbName)
    {
        try
        {
            using var conn = _db.Create(dbName);
            var connStr = conn.ConnectionString;
            var start = connStr.IndexOf("Data Source=", StringComparison.OrdinalIgnoreCase) + 12;
            var end = connStr.IndexOf(';', start);
            return end < 0 ? connStr[start..] : connStr[start..end];
        }
        catch { return null; }
    }
}
