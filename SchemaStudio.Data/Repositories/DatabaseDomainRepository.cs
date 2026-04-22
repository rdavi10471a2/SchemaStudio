using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.Data.Models;

namespace SchemaStudio.Data.Repositories;

public sealed class DatabaseDomainRepository
{
    private readonly string _connectionString;

    public DatabaseDomainRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<DatabaseDomainDefinition>> GetByDatabaseIdAsync(int databaseId)
    {
        await using var connection = new SqlConnection(_connectionString);

        const string sql = """
SELECT
    DatabaseDomainId,
    DatabaseId,
    Domain,
    Description
FROM dbo.DatabaseDomain
WHERE DatabaseId = @databaseId
ORDER BY Domain;
""";

        var rows = await connection.QueryAsync<DatabaseDomainDefinition>(sql, new { databaseId });
        return rows.ToList();
    }

    public async Task<DatabaseDomainDefinition?> GetByIdAsync(int databaseDomainId)
    {
        await using var connection = new SqlConnection(_connectionString);

        const string sql = """
SELECT
    DatabaseDomainId,
    DatabaseId,
    Domain,
    Description
FROM dbo.DatabaseDomain
WHERE DatabaseDomainId = @databaseDomainId;
""";

        return await connection.QueryFirstOrDefaultAsync<DatabaseDomainDefinition>(sql, new { databaseDomainId });
    }

    public async Task<int> CreateAsync(DatabaseDomainDefinition domain)
    {
        await using var connection = new SqlConnection(_connectionString);

        const string sql = """
INSERT INTO dbo.DatabaseDomain
(
    DatabaseId,
    Domain,
    Description
)
OUTPUT INSERTED.DatabaseDomainId
VALUES
(
    @DatabaseId,
    @Domain,
    @Description
);
""";

        var databaseDomainId = await connection.ExecuteScalarAsync<int>(sql, domain);
        domain.DatabaseDomainId = databaseDomainId;
        return databaseDomainId;
    }

    public async Task UpdateAsync(DatabaseDomainDefinition domain)
    {
        await using var connection = new SqlConnection(_connectionString);

        const string sql = """
UPDATE dbo.DatabaseDomain
SET
    Domain = @Domain,
    Description = @Description
WHERE DatabaseDomainId = @DatabaseDomainId;
""";

        await connection.ExecuteAsync(sql, domain);
    }

    public async Task DeleteAsync(int databaseDomainId)
    {
        await using var connection = new SqlConnection(_connectionString);

        const string sql = """
DELETE FROM dbo.DatabaseDomain
WHERE DatabaseDomainId = @databaseDomainId;
""";

        await connection.ExecuteAsync(sql, new { databaseDomainId });
    }
}
