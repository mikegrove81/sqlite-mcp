using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;
using SqliteMcp.Services;

namespace SqliteMcp.Tools;

[McpServerToolType]
public class ImportTools
{
    private readonly SqliteConnectionFactory _db;

    public ImportTools(SqliteConnectionFactory db) => _db = db;

    private static readonly string[] SearchPaths =
    [
        Environment.CurrentDirectory,
        Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
    ];

    private static string ResolveFilePath(string filePath)
    {
        if (filePath.Contains(Path.DirectorySeparatorChar) ||
            filePath.Contains(Path.AltDirectorySeparatorChar) ||
            Path.IsPathRooted(filePath))
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"CSV file not found: {filePath}");
            return filePath;
        }

        foreach (var dir in SearchPaths)
        {
            var candidate = Path.Combine(dir, filePath);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            $"CSV file '{filePath}' not found in any default location:\n" +
            string.Join("\n", SearchPaths.Select(p => $"  - {p}")));
    }

    [McpServerTool(Name = "sqlite_import_csv", Destructive = true)]
    [Description(
        "Import a CSV file into a SQLite table. Auto-creates the table and analyzes column types. " +
        "If the table already exists it is dropped and recreated. All columns are nullable. " +
        "If only a filename is given (no path), searches the current directory, temp folder, Downloads, and Desktop.")]
    public async Task<string> ImportCsv(
        [Description("CSV filename or full path")] string filePath,
        [Description("Table name (optional — derived from filename if omitted)")] string? table = null,
        [Description("Database name as configured in appsettings.json (e.g. 'plex'). Defaults to the configured default.")] string? database = null,
        [Description("First data row to import, 1-based (default: 1)")] int startRow = 1,
        [Description("Column delimiter (default: comma). Use 'tab' for tab-delimited.")] string delimiter = ",",
        CancellationToken ct = default)
    {
        filePath = ResolveFilePath(filePath);

        if (string.IsNullOrWhiteSpace(table))
            table = SanitizeTableName(Path.GetFileNameWithoutExtension(filePath));

        var actualDelimiter = delimiter.Equals("tab", StringComparison.OrdinalIgnoreCase) ? "\t" : delimiter;

        var dataTable = new DataTable();

        using (var reader = new StreamReader(filePath))
        {
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = actualDelimiter,
                HasHeaderRecord = true,
                MissingFieldFound = null,
                BadDataFound = null,
                TrimOptions = TrimOptions.Trim
            };

            using var csv = new CsvReader(reader, csvConfig);
            await csv.ReadAsync();
            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? throw new InvalidOperationException("CSV has no header row.");

            for (int i = 0; i < headers.Length; i++)
                dataTable.Columns.Add(SanitizeColumnName(headers[i], i), typeof(string));

            int currentRow = 0;
            while (await csv.ReadAsync())
            {
                ct.ThrowIfCancellationRequested();
                currentRow++;
                if (currentRow < startRow) continue;

                var row = dataTable.NewRow();
                for (int i = 0; i < headers.Length; i++)
                {
                    var value = i < csv.Parser.Count ? csv.GetField(i) : null;
                    row[i] = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
                }
                dataTable.Rows.Add(row);
            }
        }

        if (dataTable.Rows.Count == 0)
            return $"CSV file has headers but no data rows (startRow={startRow}).";

        var rowCount = dataTable.Rows.Count;

        await using var conn = _db.Create(database);
        await conn.OpenAsync(ct);

        // Drop if exists
        await using (var drop = new SqliteCommand($"DROP TABLE IF EXISTS [{table}]", conn) { CommandTimeout = 30 })
            await drop.ExecuteNonQueryAsync(ct);

        // Analyze types from data
        var columnTypes = AnalyzeColumnTypes(dataTable);

        // Create table
        var createSql = BuildCreateTable(table, dataTable.Columns, columnTypes);
        await using (var create = new SqliteCommand(createSql, conn) { CommandTimeout = 30 })
            await create.ExecuteNonQueryAsync(ct);

        // Insert rows in batches
        await using var tx = await conn.BeginTransactionAsync(ct);
        var colList = string.Join(", ", dataTable.Columns.Cast<DataColumn>().Select(c => $"[{c.ColumnName}]"));
        var paramList = string.Join(", ", dataTable.Columns.Cast<DataColumn>().Select((_, i) => $"@p{i}"));
        var insertSql = $"INSERT INTO [{table}] ({colList}) VALUES ({paramList})";

        await using var insertCmd = new SqliteCommand(insertSql, conn, (SqliteTransaction)tx) { CommandTimeout = 120 };
        for (int i = 0; i < dataTable.Columns.Count; i++)
            insertCmd.Parameters.Add($"@p{i}", SqliteType.Text);

        foreach (DataRow row in dataTable.Rows)
        {
            for (int i = 0; i < dataTable.Columns.Count; i++)
                insertCmd.Parameters[$"@p{i}"].Value = row[i] == DBNull.Value ? DBNull.Value : row[i];
            await insertCmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("## CSV Import Complete");
        sb.AppendLine();
        sb.AppendLine($"- **File:** `{filePath}`");
        sb.AppendLine($"- **Database:** {database ?? _db.DefaultDatabase}");
        sb.AppendLine($"- **Table:** `{table}`");
        sb.AppendLine($"- **Rows imported:** {rowCount:N0}");
        sb.AppendLine();
        sb.AppendLine("### Column Types");
        sb.AppendLine();
        sb.AppendLine("| Column | Type |");
        sb.AppendLine("|--------|------|");
        for (int i = 0; i < dataTable.Columns.Count; i++)
            sb.AppendLine($"| {dataTable.Columns[i].ColumnName} | {columnTypes[i]} |");
        sb.AppendLine();
        sb.AppendLine("### CREATE TABLE Script");
        sb.AppendLine();
        sb.AppendLine("```sql");
        sb.AppendLine(createSql);
        sb.AppendLine("```");

        return sb.ToString();
    }

    private static List<string> AnalyzeColumnTypes(DataTable dataTable)
    {
        var types = new List<string>();

        foreach (DataColumn col in dataTable.Columns)
        {
            var values = dataTable.Rows.Cast<DataRow>()
                .Select(r => r[col] == DBNull.Value ? null : r[col]?.ToString())
                .Where(v => v != null)
                .ToList();

            if (values.Count == 0) { types.Add("TEXT"); continue; }

            if (values.All(v => long.TryParse(v, out _)))
            {
                types.Add("INTEGER");
            }
            else if (values.All(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                types.Add("REAL");
            }
            else
            {
                types.Add("TEXT");
            }
        }

        return types;
    }

    private static string BuildCreateTable(string table, DataColumnCollection columns, List<string> types)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE [{table}]");
        sb.AppendLine("(");
        for (int i = 0; i < columns.Count; i++)
        {
            sb.Append($"    [{columns[i].ColumnName}] {types[i]} NULL");
            if (i < columns.Count - 1) sb.Append(',');
            sb.AppendLine();
        }
        sb.Append(");");
        return sb.ToString();
    }

    private static string SanitizeTableName(string name)
    {
        var sanitized = Regex.Replace(name, @"[^\w]", "_");
        sanitized = Regex.Replace(sanitized, @"_+", "_").Trim('_');
        return string.IsNullOrEmpty(sanitized) ? "imported_data" : sanitized;
    }

    private static string SanitizeColumnName(string header, int index)
    {
        if (string.IsNullOrWhiteSpace(header)) return $"Column{index + 1}";
        var sanitized = Regex.Replace(header, @"[^\w\s]", "");
        sanitized = Regex.Replace(sanitized, @"\s+", "_").Trim('_');
        return string.IsNullOrEmpty(sanitized) ? $"Column{index + 1}" : sanitized;
    }
}
