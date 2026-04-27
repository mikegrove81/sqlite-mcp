using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SqliteMcp.Services;

namespace SqliteMcp.Tools;

[McpServerToolType]
public class QueryTools
{
    private readonly SqliteConnectionFactory _db;

    public QueryTools(SqliteConnectionFactory db) => _db = db;

    private static readonly HashSet<string> BlockedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "TRUNCATE",
        "ATTACH", "DETACH", "PRAGMA"
    };

    private static void ValidateReadOnly(string query)
    {
        var trimmed = query.TrimStart();
        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException("Query tool only accepts SELECT or WITH (CTE) statements. Use sqlite_execute for write operations.");
        }

        foreach (var keyword in BlockedKeywords)
        {
            var idx = trimmed.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var before = idx > 0 ? trimmed[idx - 1] : ' ';
                var after = idx + keyword.Length < trimmed.Length ? trimmed[idx + keyword.Length] : ' ';
                if (!char.IsLetterOrDigit(before) && before != '_' &&
                    !char.IsLetterOrDigit(after) && after != '_')
                {
                    throw new McpException($"Query contains blocked keyword '{keyword}'. Use the appropriate tool for write/DDL operations.");
                }
            }
        }
    }

    [McpServerTool(Name = "sqlite_query", ReadOnly = true)]
    [Description("Execute a SELECT query against a SQLite database and return results as a markdown table.")]
    public async Task<string> Query(
        [Description("SQL SELECT query to execute")] string query,
        [Description("Database name as configured in appsettings.json (e.g. 'plex'). Defaults to the configured default.")] string? database = null,
        [Description("Maximum rows to return (default: 1000)")] int maxRows = 1000,
        CancellationToken ct = default)
    {
        ValidateReadOnly(query);

        await using var conn = _db.Create(database);
        await conn.OpenAsync(ct);
        await using var cmd = new SqliteCommand(query, conn);
        cmd.CommandTimeout = 120;
        await using var rdr = await cmd.ExecuteReaderAsync(ct);

        var sb = new StringBuilder();
        var colCount = rdr.FieldCount;

        sb.Append('|');
        for (int i = 0; i < colCount; i++)
            sb.Append($" {rdr.GetName(i)} |");
        sb.AppendLine();

        sb.Append('|');
        for (int i = 0; i < colCount; i++)
            sb.Append("------|");
        sb.AppendLine();

        int rowCount = 0;
        while (await rdr.ReadAsync(ct) && rowCount < maxRows)
        {
            sb.Append('|');
            for (int i = 0; i < colCount; i++)
            {
                var val = rdr.IsDBNull(i) ? "NULL" : rdr.GetValue(i)?.ToString() ?? "";
                sb.Append($" {val} |");
            }
            sb.AppendLine();
            rowCount++;
        }

        sb.AppendLine();
        var truncated = rowCount >= maxRows ? $" (truncated at {maxRows})" : "";
        sb.AppendLine($"*{rowCount} rows returned{truncated}.*");
        return sb.ToString();
    }

    [McpServerTool(Name = "sqlite_query_json", ReadOnly = true)]
    [Description("Execute a SELECT query against a SQLite database and return results as a JSON array. Better for structured data processing.")]
    public async Task<string> QueryJson(
        [Description("SQL SELECT query to execute")] string query,
        [Description("Database name as configured in appsettings.json (e.g. 'plex'). Defaults to the configured default.")] string? database = null,
        [Description("Maximum rows to return (default: 1000)")] int maxRows = 1000,
        CancellationToken ct = default)
    {
        ValidateReadOnly(query);

        await using var conn = _db.Create(database);
        await conn.OpenAsync(ct);
        await using var cmd = new SqliteCommand(query, conn);
        cmd.CommandTimeout = 120;
        await using var rdr = await cmd.ExecuteReaderAsync(ct);

        var results = new List<Dictionary<string, object?>>();
        int rowCount = 0;

        while (await rdr.ReadAsync(ct) && rowCount < maxRows)
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < rdr.FieldCount; i++)
                row[rdr.GetName(i)] = rdr.IsDBNull(i) ? null : rdr.GetValue(i);
            results.Add(row);
            rowCount++;
        }

        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        var truncated = rowCount >= maxRows ? $"\n\n*Truncated at {maxRows} rows.*" : "";
        return $"{json}{truncated}\n\n*{rowCount} rows returned.*";
    }
}
