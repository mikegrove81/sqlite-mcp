using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace SqliteMcp.Services;

public class SqliteConnectionFactory
{
    private readonly Dictionary<string, string> _databasePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _defaultDatabase;

    public SqliteConnectionFactory(IConfiguration configuration)
    {
        _defaultDatabase = configuration["DefaultDatabase"] ?? "default";

        var section = configuration.GetSection("Databases");
        foreach (var child in section.GetChildren())
        {
            if (!string.IsNullOrEmpty(child.Value))
                _databasePaths[child.Key] = child.Value;
        }

        var envPath = Environment.GetEnvironmentVariable("SQLITE_DATABASE_PATH");
        if (!string.IsNullOrEmpty(envPath))
            _databasePaths[_defaultDatabase] = envPath;

        if (!_databasePaths.ContainsKey(_defaultDatabase))
            _databasePaths[_defaultDatabase] = Path.Combine(Environment.CurrentDirectory, "default.db");
    }

    public string DefaultDatabase => _defaultDatabase;

    public IReadOnlyCollection<string> AvailableDatabases =>
        _databasePaths.Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .Select(kvp => kvp.Key).ToList();

    public SqliteConnection Create(string? database = null)
    {
        var dbName = database ?? _defaultDatabase;

        if (!_databasePaths.TryGetValue(dbName, out var path) || string.IsNullOrEmpty(path))
        {
            throw new InvalidOperationException(
                $"Database '{dbName}' is not configured. Available: {string.Join(", ", AvailableDatabases)}");
        }

        return new SqliteConnection($"Data Source={path};");
    }
}
