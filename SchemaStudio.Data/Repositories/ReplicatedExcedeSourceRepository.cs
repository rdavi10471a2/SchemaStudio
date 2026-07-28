using Dapper;
using Microsoft.Data.SqlClient;
using SchemaStudio.Data.Models;

namespace SchemaStudio.Data.Repositories;

/// <summary>
/// Reads control information from VVG_Silver. Uses the default connection string; the table is
/// referenced by its three-part name so it resolves regardless of the connection's initial database.
/// </summary>
public sealed class ReplicatedExcedeSourceRepository
{
    private readonly string _connectionString;

    public ReplicatedExcedeSourceRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>Returns every replicated Excede source (CountryDB, ReplicatedDBName), ordered by database name.</summary>
    public async Task<IReadOnlyList<ReplicatedExcedeSource>> GetAllAsync()
    {
        await using (var connection = new SqlConnection(_connectionString))
        {
            const string sql = """
SELECT
    CountryDB,
    ReplicatedDBName
FROM VVG_Silver.dbo.ReplicatedExcedeSources
ORDER BY ReplicatedDBName;
""";

            var rows = await connection.QueryAsync<ReplicatedExcedeSource>(sql);
            return rows.ToList();
        }
    }
}
