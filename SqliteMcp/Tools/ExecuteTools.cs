using System.ComponentModel;
using System.Text;
using Microsoft.Data.Sqlite;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SqliteMcp.Services;

namespace SqliteMcp.Tools;

[McpServerToolType]
public class ExecuteTools
{
    private readonly SqliteConnectionFactory _db;

    public ExecuteTools(SqliteConnectionFactory db) => _db = db;

    private static readonly HashSet<string> BlockedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ATTACH", "DETACH"
    };

    private static readonly HashSet<string> DdlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CREATE", "ALTER", "DROP"
    };

    private static void ValidateExecute(string statement)
    {
        var trimmed = statement.TrimStart();

        foreach (var keyword in BlockedKeywords)
        {
            if (trimmed.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                throw new McpException($"Blocked keyword '{keyword}' detected.");
        }

        foreach (var keyword in DdlKeywords)
        {
            if (trimmed.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
                throw new McpException($"DDL statement detected ('{keyword}'). Use sqlite_execute_ddl for schema changes.");
        }
    }

    private static void ValidateDdl(string statement)
    {
        var trimmed = statement.TrimStart();

        foreach (var keyword in BlockedKeywords)
        {
            if (trimmed.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                throw new McpException($"Blocked keyword '{keyword}' detected.");
        }

        var startsWithDdl = DdlKeywords.Any(k => trimmed.StartsWith(k, StringComparison.OrdinalIgnoreCase));
        if (!startsWithDdl)
            throw new McpException("DDL tool only accepts CREATE, ALTER, or DROP statements.");
    }

    [McpServerTool(Name = "sqlite_execute", Destructive = true)]
    [Description("Execute an INSERT, UPDATE, or DELETE statement against a SQLite database. Returns rows affected. NOT for DDL — use sqlite_execute_ddl for schema changes.")]
    public async Task<string> Execute(
        [Description("SQL statement to execute (INSERT, UPDATE, DELETE)")] string statement,
        [Description("Database name as configured in appsettings.json (e.g. 'plex'). Defaults to the configured default.")] string? database = null,
        CancellationToken ct = default)
    {
        ValidateExecute(statement);

        await using var conn = _db.Create(database);
        await conn.OpenAsync(ct);
        await using var cmd = new SqliteCommand(statement, conn);
        cmd.CommandTimeout = 120;
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return $"Executed successfully. {rows} row(s) affected.\n\nDatabase: [{database ?? _db.DefaultDatabase}]";
    }

    [McpServerTool(Name = "sqlite_execute_ddl", Destructive = true)]
    [Description("Execute a DDL statement (CREATE, ALTER, DROP) against a SQLite database. Use this for schema changes.")]
    public async Task<string> ExecuteDdl(
        [Description("DDL statement to execute")] string statement,
        [Description("Database name as configured in appsettings.json (e.g. 'plex'). Defaults to the configured default.")] string? database = null,
        CancellationToken ct = default)
    {
        ValidateDdl(statement);

        await using var conn = _db.Create(database);
        await conn.OpenAsync(ct);
        await using var cmd = new SqliteCommand(statement, conn);
        cmd.CommandTimeout = 120;
        await cmd.ExecuteNonQueryAsync(ct);
        return $"DDL executed successfully on [{database ?? _db.DefaultDatabase}].\n\nStatement:\n```sql\n{statement}\n```";
    }
}
