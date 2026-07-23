using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudioWebViewer.Models;

namespace SchemaStudioWebViewer.Data
{
    public class ReadOnlyDatabaseRepository
    {
        private readonly string _connectionString;

        public ReadOnlyDatabaseRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<List<DatabaseModel>> GetAllAsync()
        {
            await using SqlConnection conn = new(_connectionString);

            const string sql = @"
SELECT
    DatabaseId,
    DatabaseName,
    DefaultSchema,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    ViewNameFilter,
    Active
FROM Databases
ORDER BY DatabaseName";

            var result = await conn.QueryAsync<DatabaseModel>(sql);

            return result.ToList();
        }

        public async Task<DatabaseModel?> GetByIdAsync(int databaseId)
        {
            await using SqlConnection conn = new(_connectionString);

            const string sql = @"
SELECT
    DatabaseId,
    DatabaseName,
    DefaultSchema,
    BusinessName,
    BusinessDescription,
    DeveloperNotes,
    ViewNameFilter,
    Active
FROM Databases
WHERE DatabaseId = @databaseId";

            return await conn.QueryFirstOrDefaultAsync<DatabaseModel>(
                sql,
                new { databaseId });
        }

        public async Task<List<DatabaseDomainModel>> GetDomainsAsync(int databaseId)
        {
            await using SqlConnection conn = new(_connectionString);

            const string sql = @"
SELECT
    DatabaseDomainId,
    DatabaseId,
    Domain
FROM DatabaseDomain
WHERE DatabaseId = @databaseId
ORDER BY Domain";

            var result = await conn.QueryAsync<DatabaseDomainModel>(
                sql,
                new { databaseId });

            return result.ToList();
        }
    }
}
