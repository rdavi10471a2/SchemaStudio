using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;

namespace SchemaStudio.Data.Repositories;

[FileVersion("1.5")]
[AIFileContext("SchemaStudio.Data/Repositories/DatabaseRepository.cs", "Read/write repository for Schema Studio database metadata records.", Responsibilities = "Loads and maintains dbo.Databases rows for maintenance screens and downstream schema tools.", Nuances = "Applies small additive metadata table upgrades before database reads and writes so UI fields can roll out without a separate migration step.", LastReviewed = "2026-05-11")]
public sealed class DatabaseRepository
{
    private readonly string _connectionString;

    public DatabaseRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<DatabaseDefinition>> GetAllAsync()
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            await EnsureDatabaseMetadataColumnsAsync(connection);

            const string sql = """
SELECT
    DatabaseId,
    DatabaseName,
    DefaultSchema,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    ViewNameFilter,
    SQLLookupString,
    Active
FROM dbo.Databases
ORDER BY DatabaseName;
""";

            var rows = await connection.QueryAsync<DatabaseDefinition>(sql);
            return rows.ToList();
        }
    }

    public async Task<DatabaseDefinition?> GetByIdAsync(int databaseId)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            await EnsureDatabaseMetadataColumnsAsync(connection);

            const string sql = """
SELECT
    DatabaseId,
    DatabaseName,
    DefaultSchema,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    ViewNameFilter,
    SQLLookupString,
    Active
FROM dbo.Databases
WHERE DatabaseId = @databaseId;
""";

            return await connection.QueryFirstOrDefaultAsync<DatabaseDefinition>(sql, new { databaseId });
        }
    }

    public async Task<int> CreateAsync(DatabaseDefinition database)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            await EnsureDatabaseMetadataColumnsAsync(connection);

            const string sql = """
INSERT INTO dbo.Databases
(
    DatabaseName,
    DefaultSchema,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    ViewNameFilter,
    SQLLookupString,
    Active
)
OUTPUT INSERTED.DatabaseId
VALUES
(
    @DatabaseName,
    @DefaultSchema,
    @BusinessName,
    @BusinessDescription,
    @DeveloperNotes,
    @ViewNameFilter,
    @SQLLookupString,
    @Active
);
""";

            var databaseId = await connection.ExecuteScalarAsync<int>(sql, database);
            database.DatabaseId = databaseId;
            return databaseId;
        }
    }

    public async Task UpdateAsync(DatabaseDefinition database)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            await EnsureDatabaseMetadataColumnsAsync(connection);

            const string sql = """
UPDATE dbo.Databases
SET
    DatabaseName = @DatabaseName,
    DefaultSchema = @DefaultSchema,
    BusinessName = @BusinessName,
    BusinessDescription = @BusinessDescription,
    DeveloperNotes = @DeveloperNotes,
    ViewNameFilter = @ViewNameFilter,
    SQLLookupString = @SQLLookupString,
    Active = @Active
WHERE DatabaseId = @DatabaseId;
""";

            await connection.ExecuteAsync(sql, database);
        }
    }

    public async Task DeleteAsync(int databaseId)
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
DELETE FROM dbo.Databases
WHERE DatabaseId = @databaseId;
""";

            await connection.ExecuteAsync(sql, new { databaseId });
        }
    }

    private static async Task EnsureDatabaseMetadataColumnsAsync(SqlConnection connection)
    {
        const string sql = """
IF COL_LENGTH('dbo.Databases', 'SQLLookupString') IS NULL
BEGIN
    ALTER TABLE dbo.Databases
        ADD SQLLookupString nvarchar(500) NULL;
END;
""";

        await connection.ExecuteAsync(sql);
    }
}
