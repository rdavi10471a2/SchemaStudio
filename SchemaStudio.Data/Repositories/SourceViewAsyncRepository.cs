using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.Data.Models;

namespace SchemaStudio.Data.Repositories;

public sealed class SourceViewAsyncRepository
{
    private readonly string _connectionString;

    public SourceViewAsyncRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<SourceViewDefinition>> GetByDatabaseAsync(string databaseName, string? viewNameFilter)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new ArgumentException("Database name is required.", nameof(databaseName));
        }

        await using (var connection = new SqlConnection(_connectionString))
        {
            var quotedDatabase = QuoteSqlIdentifier(databaseName);
            var hasFilter = !string.IsNullOrWhiteSpace(viewNameFilter);
            var normalizedFilter = NormalizeStartsWithFilter(viewNameFilter);

            var sql = $"""
SELECT
    DatabaseName = CAST(@databaseName AS sysname),
    SchemaName = CAST(s.name AS sysname),
    ObjectName = CAST(v.name AS sysname),
    ModifyDate = v.modify_date
FROM {quotedDatabase}.sys.views v
INNER JOIN {quotedDatabase}.sys.schemas s
    ON v.schema_id = s.schema_id
WHERE (@hasFilter = 0 OR v.name LIKE @normalizedFilter ESCAPE '\')
ORDER BY s.name, v.name;
""";

            var rows = await connection.QueryAsync<SourceViewDefinition>(
                sql,
                new
                {
                    databaseName,
                    hasFilter,
                    normalizedFilter
                });

            return rows.ToList();
        }
    }

    private static string NormalizeStartsWithFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "%";
        }

        var prefix = value.Trim().TrimEnd('%');
        return EscapeLike(prefix) + "%";
    }

    private static string EscapeLike(string value)
    {
        return value
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_")
            .Replace("[", @"\[");
    }

    private static string QuoteSqlIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]")}]";
    }
}
